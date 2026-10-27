# Instalador Windows do OpenBVE by LT ARTS

Assistente independente em português, com seis etapas, instalação por usuário
em `Environment.SpecialFolder.MyDocuments\OpenBVE by LT ARTS`, ícones opcionais
na área de trabalho e entradas no menu Iniciar. A pasta Documentos segue a
localização configurada no Windows, inclusive quando redirecionada ao OneDrive.

O pacote contém o simulador .NET 10 autocontido, a Mobitrens e suas dependências,
o trem do jogador, os XMLs dos serviços IA e os anúncios disponíveis. O próprio
assistente usa o .NET Framework do Windows e não baixa dependências. O executável
único incorpora o ZIP como recurso; não depende do Inno Setup instalado no PC
de desenvolvimento. Não pede elevação administrativa.

O aplicativo principal compila os formulários originais de
`source/OpenBVE/UserInterface`, incluindo `formMain.Designer.cs` e os recursos
originais. Não há launcher alternativo. A geração de fontes no diretório obj
adapta somente os tipos gráficos e as chamadas de integração Windows ao motor
nativo. O layout e os handlers de seleção, opções e controles são os originais.
O Route Editor é distribuído separadamente em `Editor`, com atalho próprio.

## Compilar

Execute pelo PowerShell 7, na raiz do repositório:

```powershell
./installers/windows/ltarts/build-installer.ps1
```

Use `-RebuildRuntime` para também recompilar o simulador. Sem esse parâmetro,
é empacotada a build validada em `native/dotnet10/openbve-windows-beta`.
O validador `TextureAudit` deve estar compilado. O script usa o compilador
do .NET Framework do Windows e gera o EXE em `native/dotnet10/artifacts`,
com SHA-256 e relatório de validação ao lado.

Por padrão, todas as imagens são decodificadas com o plugin exato do pacote,
e o instalador gerado é executado em modo de verificação numa pasta de teste
com espaços, acentos e caminhos longos. Todos os arquivos extraídos são
conferidos pelo manifesto SHA-256. Esse modo não grava atalhos nem registro.
`-SkipInstallTest` serve apenas para desenvolvimento e desativa essa verificação.

`SetupChecks.cs` valida a aceitação da licença, a recusa de caminhos que saem
do destino, a criação de atalhos e a preservação de arquivos novos/editados na
desinstalação. Também renderiza as seis páginas para revisão visual.

`refresh-local-build.ps1 -WorkDirectory ...` atualiza apenas os binários do
assistente e desinstalador num payload já validado, após alterações no instalador.
Não atualiza o motor, a rota nem as texturas; para esses casos use o build completo.

## Instalação e remoção

O assistente recusa uma pasta que já contém arquivos e uma segunda instalação
registrada, para não sobrescrever projetos existentes. Esta beta não oferece
atualização no mesmo diretório. O desinstalador remove apenas arquivos cujo
SHA-256 continua igual ao instalado, preserva arquivos novos/modificados e
`App/UserData`, e mantém o próprio executável pequeno após a remoção.
Não segue links de diretório durante a limpeza de pastas vazias.

O runtime exige Windows 10/11 x64 e OpenGL compatível. A interface foi
renderizada em teste local; o controle interativo do desktop não estava
disponível nesta sessão. A extração e o motor são validados separadamente.
