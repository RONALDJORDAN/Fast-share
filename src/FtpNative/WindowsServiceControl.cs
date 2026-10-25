using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FtpNative;

public enum ServiceState
{
    Unknown = 0,
    Stopped = 1,
    StartPending = 2,
    StopPending = 3,
    Running = 4,
    ContinuePending = 5,
    PausePending = 6,
    Paused = 7,
}

public sealed class ServiceNotFoundException : Exception
{
    public ServiceNotFoundException(string name)
        : base($"Servico '{name}' nao esta instalado.")
    {
    }
}

public static class WindowsServiceControl
{
    private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_STOP = 0x0020;
    private const uint SERVICE_WIN32_OWN_PROCESS = 0x10;
    private const uint SERVICE_AUTO_START = 0x02;
    private const uint SERVICE_ERROR_NORMAL = 0x01;
    private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    private const uint SERVICE_CONTROL_STOP = 0x01;
    private const uint SERVICE_CONFIG_DESCRIPTION = 1;
    private const uint SERVICE_CONFIG_FAILURE_ACTIONS = 2;
    private const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
    private const int ERROR_SERVICE_EXISTS = 1073;
    private const int ERROR_SERVICE_ALREADY_RUNNING = 1056;
    private const int ERROR_SERVICE_NOT_ACTIVE = 1062;
    private const int SC_ACTION_RESTART = 1;

    public static ServiceState GetState(string serviceName)
    {
        var manager = OpenManager();
        try
        {
            var service = OpenService(manager, serviceName, SERVICE_QUERY_STATUS);
            if (service == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ERROR_SERVICE_DOES_NOT_EXIST)
                {
                    throw new ServiceNotFoundException(serviceName);
                }

                throw new Win32Exception(error);
            }

            try
            {
                return QueryState(service);
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    public static bool TryGetState(string serviceName, out ServiceState state)
    {
        try
        {
            state = GetState(serviceName);
            return true;
        }
        catch (ServiceNotFoundException)
        {
            state = ServiceState.Unknown;
            return false;
        }
    }

    public static void CreateOrUpdate(string serviceName, string displayName, string description, string binaryPath)
    {
        var manager = OpenManager();
        try
        {
            var service = CreateService(
                manager,
                serviceName,
                displayName,
                SERVICE_ALL_ACCESS,
                SERVICE_WIN32_OWN_PROCESS,
                SERVICE_AUTO_START,
                SERVICE_ERROR_NORMAL,
                binaryPath,
                null,
                IntPtr.Zero,
                null,
                null,
                null);

            if (service == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ERROR_SERVICE_EXISTS)
                {
                    throw new Win32Exception(error, "Nao foi possivel criar o servico.");
                }

                service = OpenService(manager, serviceName, SERVICE_ALL_ACCESS);
                if (service == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                if (!ChangeServiceConfig(
                        service,
                        SERVICE_NO_CHANGE,
                        SERVICE_AUTO_START,
                        SERVICE_NO_CHANGE,
                        binaryPath,
                        null,
                        IntPtr.Zero,
                        null,
                        null,
                        null,
                        displayName))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Nao foi possivel atualizar o servico.");
                }
            }

            try
            {
                SetDescription(service, description);
                SetFailureActions(service);
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    public static void Start(string serviceName)
    {
        var manager = OpenManager();
        try
        {
            var service = OpenService(manager, serviceName, SERVICE_ALL_ACCESS);
            if (service == IntPtr.Zero)
            {
                throw ServiceError(serviceName);
            }

            try
            {
                if (!StartService(service, 0, IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ERROR_SERVICE_ALREADY_RUNNING)
                    {
                        throw new Win32Exception(error, "Nao foi possivel iniciar o servico.");
                    }
                }
            }
            finally
            {
                CloseServiceHandle(service);
            }

            WaitForState(serviceName, ServiceState.Running, 60000);
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    public static void Stop(string serviceName, int timeoutMs = 60000)
    {
        var manager = OpenManager();
        try
        {
            var service = OpenService(manager, serviceName, SERVICE_QUERY_STATUS | SERVICE_STOP);
            if (service == IntPtr.Zero)
            {
                throw ServiceError(serviceName);
            }

            try
            {
                var status = default(SERVICE_STATUS);
                if (!ControlService(service, SERVICE_CONTROL_STOP, ref status))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ERROR_SERVICE_NOT_ACTIVE || error == ERROR_SERVICE_DOES_NOT_EXIST)
                    {
                        return;
                    }

                    throw new Win32Exception(error, "Nao foi possivel parar o servico.");
                }
            }
            finally
            {
                CloseServiceHandle(service);
            }

            WaitForState(serviceName, ServiceState.Stopped, timeoutMs);
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    public static void Remove(string serviceName)
    {
        var manager = OpenManager();
        try
        {
            var service = OpenService(manager, serviceName, SERVICE_ALL_ACCESS);
            if (service == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ERROR_SERVICE_DOES_NOT_EXIST)
                {
                    return;
                }

                throw new Win32Exception(error);
            }

            try
            {
                if (!DeleteService(service))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ERROR_SERVICE_DOES_NOT_EXIST)
                    {
                        throw new Win32Exception(error, "Nao foi possivel remover o servico.");
                    }
                }
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    public static void WaitForState(string serviceName, ServiceState target, int timeoutMs)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var state = GetState(serviceName);
            if (state == target)
            {
                return;
            }

            if (watch.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException($"O servico '{serviceName}' nao chegou ao estado {target} em {timeoutMs / 1000}s.");
            }

            Thread.Sleep(400);
        }
    }

    private static ServiceState QueryState(IntPtr service)
    {
        var status = default(SERVICE_STATUS);
        if (!QueryServiceStatus(service, ref status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return (ServiceState)status.CurrentState;
    }

    private static IntPtr OpenManager()
    {
        var manager = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
        if (manager == IntPtr.Zero || manager == new IntPtr(-1))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Nao foi possivel abrir o gerenciador de servicos. Execute como administrador.");
        }

        return manager;
    }

    private static Exception ServiceError(string serviceName)
    {
        var error = Marshal.GetLastWin32Error();
        return error == ERROR_SERVICE_DOES_NOT_EXIST
            ? new ServiceNotFoundException(serviceName)
            : new Win32Exception(error);
    }

    private static void SetDescription(IntPtr service, string description)
    {
        var info = new SERVICE_DESCRIPTION { Description = description };
        ChangeServiceConfig2Description(service, SERVICE_CONFIG_DESCRIPTION, ref info);
    }

    private static void SetFailureActions(IntPtr service)
    {
        var actions = new[]
        {
            new SC_ACTION { Type = SC_ACTION_RESTART, Delay = 15000 },
            new SC_ACTION { Type = SC_ACTION_RESTART, Delay = 30000 },
            new SC_ACTION { Type = SC_ACTION_RESTART, Delay = 60000 },
        };

        var size = Marshal.SizeOf<SC_ACTION>();
        var pointer = Marshal.AllocHGlobal(size * actions.Length);
        try
        {
            for (var i = 0; i < actions.Length; i++)
            {
                Marshal.StructureToPtr(actions[i], pointer + (i * size), false);
            }

            var info = new SERVICE_FAILURE_ACTIONS
            {
                ResetPeriod = 86400,
                RebootMessage = null,
                Command = null,
                ActionsCount = actions.Length,
                Actions = pointer,
            };

            ChangeServiceConfig2Actions(service, SERVICE_CONFIG_FAILURE_ACTIONS, ref info);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public int ServiceType;
        public int CurrentState;
        public int ControlsAccepted;
        public int Win32ExitCode;
        public int ServiceSpecificExitCode;
        public int CheckPoint;
        public int WaitHint;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SERVICE_DESCRIPTION
    {
        public string? Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SC_ACTION
    {
        public int Type;
        public int Delay;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SERVICE_FAILURE_ACTIONS
    {
        public int ResetPeriod;
        public string? RebootMessage;
        public string? Command;
        public int ActionsCount;
        public IntPtr Actions;
    }

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "CreateServiceW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateService(
        IntPtr manager,
        string serviceName,
        string? displayName,
        uint desiredAccess,
        uint serviceType,
        uint startType,
        uint errorControl,
        string binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? startName,
        string? password);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ChangeServiceConfig(
        IntPtr service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? startName,
        string? password,
        string? displayName);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "ChangeServiceConfig2W", CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfig2Description(IntPtr service, uint infoLevel, ref SERVICE_DESCRIPTION info);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "ChangeServiceConfig2W", CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfig2Actions(IntPtr service, uint infoLevel, ref SERVICE_FAILURE_ACTIONS info);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "StartServiceW", CharSet = CharSet.Unicode)]
    private static extern bool StartService(IntPtr service, int argumentCount, IntPtr arguments);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(IntPtr service, uint control, ref SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr service, ref SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr service);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
