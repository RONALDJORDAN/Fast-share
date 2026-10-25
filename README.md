# FastCloud (FtpNative)

Servidor FTP nativo para Windows (.NET 8 + WPF) que compartilha uma pasta na rede local, com interface grafica, painel web estilo nuvem (FastCloud) com QR Code e instalacao como servico do Windows (sempre ativo, iniciando junto com o PC).

## Recursos

- Interface grafica com os dados de acesso (IP, login, senha, porta) e QR Code para abrir a pagina FastCloud no celular.
- Escolha da pasta compartilhada em um clique; ela fica disponivel via FTP enquanto o servidor estiver ativo.
- Pagina web mobile em tempo real com tres modulos: **Galeria** (todas as fotos e videos), **Arquivos** (explorador com upload, download, nova pasta e exclusao) e **Dados compartilhados** (QR Code, enderecos, armazenamento e sessao).
- **Login obrigatorio na web**: usa os mesmos usuarios e senhas cadastrados no aplicativo. Sem login nao e possivel ver, baixar, enviar ou excluir arquivos. Nenhuma senha e exibida na pagina.
- Servico do Windows com inicio automatico, reinicio em caso de falha e regras de firewall criadas automaticamente.
- Usuarios com senha protegida por PBKDF2-SHA256; suporte a acesso anonimo opcional no FTP.
- Modo passivo com faixa de portas configuravel (padrao 30000-30100).

## Inicio rapido (interface)

1. Execute `FtpNative.exe` (sem argumentos).
2. Clique em **Escolher pasta...** e selecione o que deseja compartilhar.
3. Confira login/senha no card **Dados do servidor**.
4. Clique em **Iniciar servidor**.
5. Escaneie o QR Code ou acesse `http://IP:8080/` no celular/tablet. A pagina abre na tela de login: entre com o mesmo usuario e senha cadastrados no aplicativo para ver os arquivos, enviar, baixar, criar pastas e excluir.

A pagina web so libera o conteudo depois do login. O QR Code leva para a tela de entrada, portanto quem apenas escaneia nao consegue acessar os arquivos sem a senha. O acesso continua sendo por HTTP na rede local; use apenas em redes confiaveis.

Para deixar o servico sempre ativo (sem precisar da janela aberta), clique em **Instalar como servico do Windows**.

## Instalacao como servico (linha de comando)

Execute em um prompt **como administrador**:

```
FtpNative.exe --install --admin-user admin --admin-password MinhaSenha123
```

O que o instalador faz:

- copia o executavel para `C:\Program Files\FtpNative`;
- cria a configuracao em `C:\ProgramData\FtpNative\config.json`;
- registra o servico **FtpNative** (inicio automatico + reinicio em falhas);
- libera no firewall as portas FTP (21), passivas (30000-30100) e web (8080);
- inicia o servico.

Opcoes uteis do `--install`:

```
--port 21                porta FTP
--webport 8080           porta do painel web (0 desativa)
--root "D:\Compartilhar" pasta compartilhada
--dir "C:\Program Files\FtpNative"
--anonymous              permite acesso anonimo
--pasv-min 30000 --pasv-max 30100
--no-start               instala sem iniciar
```

## Comandos

```
FtpNative.exe                      abre a interface
FtpNative.exe --run                roda no console (Ctrl+C encerra)
FtpNative.exe --install            instala o servico
FtpNative.exe --uninstall          remove o servico (mantem arquivos)
FtpNative.exe --start | --stop | --restart | --status
FtpNative.exe --add-user NOME [SENHA]
FtpNative.exe --set-password NOME [SENHA]
FtpNative.exe --remove-user NOME
FtpNative.exe --list-users
FtpNative.exe --open-firewall
FtpNative.exe --help
```

Todas as operacoes de servico exigem administrador. A interface grafica eleva automaticamente quando necessario.

## Arquivos e configuracao

- Configuracao: `C:\ProgramData\FtpNative\config.json`
- Pasta padrao compartilhada: `C:\ProgramData\FtpNative\root`
- Log: `C:\ProgramData\FtpNative\log.txt`
- Instalacao: `C:\Program Files\FtpNative\FtpNative.exe`

## Compilar

Requisitos: .NET SDK 8 e Windows.

```
.\build.ps1
```

O resultado fica em `dist\FtpNative.exe` (arquivo unico, self-contained, nao precisa de .NET instalado na maquina de destino).

## Seguranca

O FTP tradicional e a pagina web (HTTP) transmitem senha e dados sem criptografia. Use apenas em redes locais confiaveis. Para exposicao na internet, prefira VPN ou um servidor FTPS/SFTP.

O painel web exige login em todas as rotas (pagina, APIs, downloads e QR Codes), mantem sessoes em memoria por 12 horas e nunca mostra a senha. O FTP continua aceitando os mesmos usuarios cadastrados.

## Remover tudo

```
FtpNative.exe --uninstall
```

Depois remova manualmente `C:\Program Files\FtpNative` e, se desejar, `C:\ProgramData\FtpNative`.
