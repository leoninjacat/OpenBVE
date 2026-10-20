# Route Viewer Native

Port Linux do visualizador real de rotas do OpenBVE, em .NET 10, sem Mono,
WinForms ou GDI+. Reutiliza os leitores CSV/RW, a geometria, as animações, o áudio
ambiente e o renderizador do projeto. Não precisa selecionar um trem.

## Instalar e abrir

O instalador `native/packaging/dist/OpenBVE-Native-linux-x86_64.flatpak` instala
**OpenBVE Native** e **Route Viewer Native** juntos, com o .NET incluído e as
bibliotecas do runtime GNOME gerenciadas pelo Flatpak. Não é necessário baixar
componentes manualmente. Numa máquina sem o runtime GNOME, a loja o obtém durante
a instalação; este arquivo não é um instalador offline completo.

Abra **Route Viewer Native** no menu de aplicativos e clique em **Abrir rota**.
Selecione um arquivo CSV/RW dentro do pacote completo da rota, incluindo
Railway/Object e Railway/Sound. O seletor resolve os caminhos temporários do
portal para preservar o acesso ao cenário. O pacote permite leitura e gravação na pasta pessoal para editar as rotas,
além de gráficos e áudio. Discos externos
precisam de permissão para a árvore completa do conteúdo.

Também é possível arrastar uma rota para a janela ou executar:

```sh
flatpak run --command=route-viewer-native org.openbve.OpenBVE.Native '/caminho/rota.csv'
```

## Interface e modos de câmera

A barra superior usa ícones com dicas ao passar o mouse. Os dois ícones no alto
à direita (trilhos e olho) alternam os modos; **Tab** também alterna. O modo
inicial é **Trilho 0**. A indicação discreta no rodapé identifica o modo atual.

### Trilho 0 — cabine invisível

A câmera é transportada pelo trilho de índice zero, usando sua posição, direção,
altura, inclinação e base lateral. Os deslocamentos são locais à cabine; não há
busca do trilho mais próximo para dirigir a câmera neste modo.

- **W/S ou Num 9/3**: avançar/recuar na via.
- **Num 4/6**: deslocar a visão lateralmente; **Num 8/2**: subir/descer.
- **Num 5 ou Home**: restaurar deslocamentos, orientação e zoom, mantendo a
  distância atual na rota. O reset não volta ao começo da linha.
- **Setas do teclado ou botão direito e arraste**: olhar ao redor da cabine.
- **Globo ou MMB e arraste**: girar a visão na cabine invisível, sem alterar
  sua posição ou os deslocamentos locais. A câmera continua acompanhando o
  trilho zero, inclusive em curvas e rampas. Home restaura a orientação.
- À direita da cena, mantenha as setas pressionadas para avançar/recuar. O botão
  de reprodução percorre o trilho automaticamente; **Espaço** reproduz/pausa.
- O slider vertical à direita posiciona a cabine ao longo do trilho zero.
- O movimento respeita os limites da via e para ao perder o foco da janela.

### Livre — movimento no espaço

A posição e a orientação da câmera são independentes da via. A âncora calculada
no trilho zero serve apenas à visibilidade e aos eventos do renderizador.

- O **globo no canto inferior direito** gira a visão a partir da posição livre
  atual da câmera, sem pivô externo e sem deslocá-la. Arraste com o botão
  esquerdo. As quatro setas em volta dele fazem **voo livre**, enquanto
  pressionadas. As setas de voo ficam desabilitadas no modo Trilho 0; o globo
  continua ativo e gira visualmente com a câmera, com eixos X/Y/Z coloridos.
- **WASD**: frente/trás e lados; **Q/E**: descer/subir; **Shift**: acelerar.
- **MMB**: girar na posição atual; **Shift+MMB**: pan; **botão direito**: olhar sem mover o olho.
- **Num 4/6 e Num 8/2**: deslocamento lateral e vertical.
- **Num 5**: orientar para o eixo Z do mundo e restaurar o zoom, preservando a
  posição livre. Não retorna à via.

### Controles comuns

- Zoom na lateral esquerda, ou pela roda do mouse. Na cabine, altera o campo de
  visão; na câmera livre, avança/recua na direção da visão.
- Setas inferiores à esquerda: subir/descer. O slider ao lado ajusta a velocidade
  em **km/h** (inicia em 20 km/h; faixa 1–180 km/h).
- Ícones superiores: abrir, recarregar, estação anterior/próxima, restaurar visão,
  aramado, eventos, mensagens e ajuda.
- Ctrl+O/F7: abrir; F5: recarregar preservando a câmera; Page Up/Down: estações;
  F: aramado; F9: mensagens; F1: ajuda; Esc: fechar painéis.
- Câmera no alto à direita: modo livre. Olho ao lado: ocultar/exibir a interface
  (F10). Papel abaixo do olho: ocultar/exibir o editor (F8). Os dois botões de
  visibilidade continuam disponíveis quando a interface está oculta.
- O card abaixo da barra superior mostra estação, distância, horários e lado da
  plataforma, além dos erros/avisos de carregamento. Clique para abrir o log.
- Uma seleção cancelada preserva a rota atual. Os eixos do OpenBVE usam Y vertical.

Não há seleção/edição de objetos, câmera ortográfica nem colisão. O desenho da
interface segue o esboço fornecido para este port; não incorpora o Blender.

## Editor de rotas

O editor e os controles 3D acompanham o esquema claro/escuro do sistema via
libadwaita e o portal de aparência, inclusive após mudanças com o app aberto.
As barras de rolagem ficam em faixas próprias, fora da área do texto.

Ao abrir uma rota, o texto CSV/RW aparece ao lado da cena. O seletor do painel
oferece **Lateral**, **Abaixo** e **Janela independente**. A janela independente
pode ser movida para outro monitor e reancorada pelo mesmo seletor. O texto,
a posição de edição e o histórico de desfazer continuam no mesmo editor.

**Salvar e atualizar**, ou **Ctrl+S com foco no editor**, grava o arquivo e
recarrega a cena preservando a câmera. Alterações não salvas pedem uma escolha
ao abrir outra rota ou fechar. Uma falha de salvamento mantém o texto editado.
A recarga F5 preserva o buffer; não aplica alterações ainda não salvas.
O editor também abre antes da leitura 3D, permitindo corrigir erros no arquivo.

O salvamento mantém a codificação, o BOM e o padrão CRLF de arquivos que usam
CRLF. Caracteres incompatíveis com a codificação original geram erro em vez de
substituição silenciosa. Alterações externas no arquivo impedem sobrescrita;
as diferenças devem ser resolvidas antes de salvar. A gravação substitui o
arquivo por um temporário no mesmo diretório, preservando permissões Unix.

Este é um editor de texto com prévia 3D, sem manipulação de objetos por gizmos,
autocompletar ou edição conjunta dos arquivos incluídos. A janela GTK hospeda a cena OpenTK em um painel redimensionável; o texto pode
ser desancorado sem recriar o buffer. A integração usa X11; em Wayland, depende de XWayland.

## Compilar e validar

Na raiz do repositório, com as ferramentas de desenvolvimento instaladas:

```sh
native/dotnet10/build-routeviewer.sh
native/dotnet10/routeviewer-runtime/RouteViewer '/caminho/rota.csv'
make -C native/linux-launcher check-editor # requer sessão X11
dotnet run --project native/dotnet10/CameraTests
dotnet run --project native/dotnet10/NativeTests
native/packaging/build-flatpak.sh
```

O build do viewer monta um runtime próprio e não atualiza o runtime de
desenvolvimento do simulador. No Flatpak os executáveis compartilham os assets e
plugins, e o armazenamento deduplica os arquivos idênticos do .NET.

Teste integrado opcional:

```sh
RouteViewer '/caminho/rota.csv' --camera-test --reload-test \
  '--switch=/caminho/outra-rota.csv' --size=960x720 --smoke=12 \
  --capture=/caminho/gravavel/captura.png
```

O teste começa a contar após o carregamento. Usa o hit-test dos controles na tela
para testar a alternância de modos e a órbita. Verifica movimento no trilho zero,
base da cabine, reset sem mudar distância, voo livre, pan, zoom, recarga sem duplicação de
objetos, preservação da câmera e troca de rota. Exige cena com objetos e faces
visíveis, grava o framebuffer e encerra. Use timeout externo para limitar erros
de carregamento. Somente `VIEWER_SMOKE_OK` com código de saída zero confirma o
resultado. Os testes de câmera também verificam polos, limites de zoom, base
ortogonal e independência do passo de tempo.

Configuração: `~/.config/RouteViewer-Native/Settings/` no build local; no Flatpak,
`~/.var/app/org.openbve.OpenBVE.Native/config/RouteViewer-Native/Settings/`.
As opções do simulador ficam separadas.

## Limitações

Port experimental. Inclui os formatos já portados (CSV/RW, CsvB3d, Animated,
Texture.Native e RiffWave); não implica paridade com todos os plugins e formatos
do visualizador antigo. TIFF e DLLs Win32 continuam sem suporte. O diálogo de
opções WinForms e o editor de cores dos caminhos de trilhos não foram portados.
O alcance inicial é 1000 m e a navegação mantém uma âncora na via principal para
a visibilidade e os eventos. Rotas muito grandes, aparelhos de mudança de via,
conteúdo distante da via principal, todas as condições de iluminação e todos os
formatos de áudio ainda precisam de validação adicional.

Para testar abertura vazia, entrada nativa e maximização, use `RouteViewer
--ui-test --capture=/caminho/gravavel/janela.png`, com timeout externo. O teste
local também clica no ícone de ajuda por um evento X11. Somente `VIEWER_UI_OK`
com saída zero confirma interatividade; processo vivo sozinho não confirma.
A renderização do viewer é limitada a 60 Hz sem espera de VSync, para evitar
bloqueio da thread de interface na apresentação GLX durante resize/maximização.

Para validar os três layouts, salvar pelo GTK e recarregar a cena, use
`RouteViewer /caminho/de/uma/COPIA-descartavel.csv --editor-test --smoke=15`.
Esse teste **acrescenta um comentário e salva o arquivo**; nunca use a rota
original. Somente `VIEWER_EDITOR_OK` e `VIEWER_SMOKE_OK`, com saída zero,
confirmam o resultado. O teste do helper também verifica edição pelo widget GTK e os diálogos de cancelar, descartar e erro de salvamento.

Regressão dos controles e da interface:

```sh
RouteViewer '/caminho/rota-com-estacoes.csv' --feedback-test --smoke=12
```

Esse teste envia eventos X11 de movimento/clique à cena embutida e move o
ponteiro entre frames. Confere arraste do slider e do globo nos dois modos,
soltura do globo sem salto da câmera, card de estação/erros, visibilidade da
interface/editor e alterações de tema claro/escuro. Restaura o tema do sistema
antes de terminar. Não grava o texto da rota. Exija `VIEWER_FEEDBACK_OK`,
`VIEWER_SMOKE_OK` e código de saída zero.
