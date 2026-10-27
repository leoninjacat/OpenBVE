# Instalação Linux nativa

O arquivo `dist/OpenBVE-Native-linux-x86_64.flatpak` instala a interface GTK e o
motor .NET 10 juntos. Abra-o na loja de aplicativos e clique em Instalar.
Depois, abra **OpenBVE Native** ou **Route Viewer Native** no menu de aplicativos. Não é necessário instalar
Mono, .NET, GTK ou OpenAL manualmente. A loja instala o runtime compartilhado
GNOME 50 automaticamente; a primeira instalação precisa de internet. O sistema
precisa ter suporte a Flatpak, sessão gráfica e driver OpenGL funcional.

Alternativa pelo terminal:

```sh
flatpak install --user ./OpenBVE-Native-linux-x86_64.flatpak
flatpak run org.openbve.OpenBVE.Native
```

O repositório inclui rotas em `Railway/` e o trem em `Train/`, armazenados em Git
comum. O Flatpak não duplica esse conteúdo: selecione-o na pasta do clone ou use
`bash native/run-demo.sh viewer` / `bash native/run-demo.sh train`.
O aplicativo tem acesso à pasta pessoal, áudio e GPU.
Conteúdo em discos externos precisa ser disponibilizado na pasta pessoal ou ter
permissão específica concedida pelo gerenciador de permissões do Flatpak.

## Instalar a partir de um clone em outra máquina

Instale `git`, `curl` e `flatpak` pelo gerenciador da distribuição.
Em seguida:

```bash
git clone --branch feat/linux-native https://github.com/leoninjacat/OpenBVE.git
cd OpenBVE
bash native/install-linux.sh
bash native/run-demo.sh viewer
```

O script usa a versão fixada em `release-version`, baixa o instalador e seu
SHA-256 na release do GitHub e instala no escopo do usuário. Funciona em Linux
x86_64 com Flatpak, OpenGL e X11/XWayland, sem exigir o SDK .NET ou um compilador.
O Flathub é adicionado no escopo do usuário para fornecer GNOME Platform 50.
O script recompõe o áudio grande a partir de `content-parts/`, validando SHA-256.
Veja [os detalhes do conteúdo](../../CONTEUDO.md), incluindo a restauração das
42 texturas BMP danificadas do pacote original.

Configurações e logs ficam em:
`~/.var/app/org.openbve.OpenBVE.Native/config/OpenBVE-Native/Settings/`.
Atualizações preservam essa pasta. A versão de desenvolvimento usa
`~/.config/OpenBVE-Native/Settings/`. Configurações antigas em `runtime/UserData`
são preservadas no disco, mas não são migradas automaticamente.

## Gerar o pacote

Em Fedora 44 x86_64 com SDK .NET 10, compilador C, make, pkg-config e os headers
GTK4/libadwaita/libxml2, além do runtime Flatpak org.gnome.Platform 50:

```sh
native/packaging/build-flatpak.sh
# Compilar e instalar em um único passo:
bash native/install-linux.sh --build
```

Esses requisitos são para quem compila; o usuário final instala só o Flatpak.
O script publica o .NET em modo self-contained, compila os plugins e o launcher,
executa os testes de metadados e verifica executável e bibliotecas dentro do
runtime de destino antes de exportar. O pacote usa o runtime GNOME 50, com libxml2 de ABI compatível incluída
privadamente. Não copie um launcher compilado em outra distribuição para esse
pacote sem repetir a validação de bibliotecas e a execução no sandbox. `dist/repo` é um repositório local para builds;
não é um canal público de atualizações.

## Estado do port

Pacote experimental do port existente, sem paridade total com a versão original.
Inclui plugins CSV/RW, Train.OpenBve, CsvB3d, Animated, RiffWave, OpenBveAts e
Texture.Native. Plugins ATS Win32 e outros formatos ainda não portados continuam
sem suporte. Veja `../dotnet10/README.md` para controles e limitações do motor.
O pacote não foi publicado no Flathub e não possui assinatura de distribuição.

O runtime .NET e dependências NuGet incluem os seus avisos de licença quando
fornecidos pelo publish; as licenças existentes do projeto acompanham o pacote
em `/app/share/licenses/openbve`. Antes de distribuição pública, revisar os
termos e avisos das dependências, incluindo SkiaSharp. ImageSharp foi removido do port nativo; o fallback Skia
não suporta TIFF.

O visualizador de rotas tem câmera livre, órbita também no modo trilho, globo
de orientação e editor de texto ancorável na lateral/abaixo ou em janela
independente. Salvar recarrega a prévia 3D. O pacote permite gravação na pasta
pessoal para editar as rotas. Veja [o guia do Route Viewer](../dotnet10/RouteViewer/README.md).

## OpenBVE Object Editor

Os builds deste commit também incluem **OpenBVE Object Editor** no menu de
aplicativos, com viewport B3D/CSV e código editável na lateral, abaixo ou em
janela independente. Ctrl+S salva e atualiza a geometria preservando a câmera.

Para instalar esta versão, use `bash native/install-linux.sh --build`.
O download padrão continua seguindo a release fixada em `release-version`;
esta novidade precisa de uma nova release para chegar por esse download.

```sh
flatpak run --command=object-editor-native org.openbve.OpenBVE.Native
# Ou abrir um objeto diretamente:
flatpak run --command=object-editor-native org.openbve.OpenBVE.Native /caminho/objeto.b3d
```

Configurações: `~/.var/app/org.openbve.OpenBVE.Native/config/ObjectEditor-Native/Settings/`.
Veja [o guia do Object Editor](../dotnet10/ObjectEditor/README.md).
