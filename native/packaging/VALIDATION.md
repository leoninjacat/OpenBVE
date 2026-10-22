# Validação local — 19/10/2026

Ambiente: Fedora 44 x86_64, SDK .NET 10.0.112, runtime da aplicação .NET
10.0.12, Flatpak 1.18.4 e org.gnome.Platform 50.

- Publish Release self-contained concluído sem ImageSharp.
- Testes do launcher: metadados, validação de caminhos, argumentos com espaços e
  metacaracteres, término normal e falha do processo: aprovados.
- Testes nativos: pixels RGBA/BGR, alpha, fontes, PNG, fallback JPEG, GIF com
  busca entre quadros e rejeição de bytes inválidos: aprovados.
- Bibliotecas ELF do launcher e motor verificadas dentro do runtime de destino.
- Desktop entry e ícone aceitos na exportação Flatpak; desktop-file-validate passou.
- Bundle instalado no escopo do usuário; menu e ícone exportados para o desktop.
- Launcher executado dentro do pacote instalado.
- Viagem local Mobitrens / Série 1000 Cofebra carregada dentro do sandbox, com
  captura real do framebuffer e término automático confirmado por NATIVE_SMOKE_OK.

A validação usa conteúdo local que não acompanha o instalador. Não representa
compatibilidade com todos os trens, rotas, GPUs e distribuições. O áudio foi
inicializado, mas não foi avaliado por escuta. Instalação numa máquina sem runtime
GNOME prévio e interação manual com a loja ainda precisam de validação adicional.

Logs desta sessão ficam em `../../../build-logs/`: `flatpak-build-gnome.log`,
`flatpak-install.log`, `flatpak-trip.log`, `native-tests.log` e
`launcher-check-ui.log`.

Documentação do formato: [bundles Flatpak](https://docs.flatpak.org/en/latest/single-file-bundles.html).

## Correção do seletor de arquivos

O uso manual revelou uma diferença não coberta pelo teste inicial: o portal
entrega um CSV isolado em `/run/user/UID/doc/ID`, sem a árvore Railway. Isso
causava `No Railway folder found` e objetos de cenário ausentes. O launcher agora
resolve os grants usando Documents.GetHostPaths tanto na seleção quanto antes
de iniciar o motor. Nenhuma permissão adicional foi concedida.

O teste de handoff executado dentro do Flatpak usou os grants reais da seleção
manual (rota noturna e diretório do trem) e confirmou que o processo recebe os
caminhos originais completos. O teste normal de argumentos e erros também passou.
Logs: `portal-handoff.log`, `portal-tests.log`, `flatpak-portal-build.log` e
`portal-fixed-trip.log`. O log manual original foi preservado em
`manual-trip-before-portal-fix.log` antes do teste de viagem.

## Route Viewer Native — 19/10/2026

Segundo aplicativo exportado pelo mesmo Flatpak, sem novo runtime manual.
Validado no pacote instalado, abrindo a rota noturna por um grant real do portal
Documents (o mesmo caminho temporário devolvido pelo seletor). O teste confirmou
15.426 objetos, 13 estações e os controles de câmera; recarregou sem duplicar
objetos e preservando a câmera, trocou para a rota diurna com 11.132 objetos e
terminou com `VIEWER_SMOKE_OK` (523 frames, faces opacas presentes e saída 0).
A captura do framebuffer foi inspecionada. Um teste local separado repetiu a
recarga e troca de rota em janela 960×720, com layout inspecionado.

Os testes matemáticos verificam órbita, pan no plano da câmera, dolly, olhar sem
mover o olho, vistas opostas, polos, limites e independência da taxa de quadros.
Os testes nativos e os testes do launcher do simulador passaram novamente.
A inicialização estática da API também foi exercitada: os eventos do proxy
Win32 usam handles anônimos em Linux, pois nomes de EventWaitHandle não são
suportados pelo .NET nesse sistema. O código original em source/ foi preservado.

Logs: `routeviewer-flatpak-trip.log`, `routeviewer-reload.log`,
`routeviewer-flatpak-build.log`, `routeviewer-flatpak-install.log`,
`camera-tests.log`, `native-tests-routeviewer.log`,
`launcher-tests-routeviewer.log` e `routeviewer-empty-start.log`.

As validações não abrangem todos os formatos e plugins do viewer original.
O áudio foi inicializado; não houve avaliação por escuta. Os controles foram
exercitados pelo mesmo hit-test e caminho de drag usados pela interface; a
interação física de todos os atalhos e dispositivos ainda não foi validada.

## Interface por ícones e câmera no trilho zero

O painel anterior foi substituído por ícones desenhados em Skia e renderizados
por uma camada OpenGL própria, sem fontes de ícones ou downloads externos. O
layout segue o esboço: ferramentas no alto, modos à direita, zoom à esquerda,
velocidade em km/h no rodapé, controles de via à direita e globo de órbita/voo no
canto inferior direito. As dicas aparecem sob o ponteiro.

Há dois modelos de navegação: cabine com offsets locais transportada pelo trilho
zero, e câmera livre em coordenadas do mundo. Os testes matemáticos verificam
posição local em curvas/rampas, reset sem mudar distância e limite da via. O teste
integrado usa o hit-test dos ícones para alternar os modos, avança a cabine,
verifica sua posição contra o TrackFollower e confirma deslocamento livre sem
restrição à via. Capturas foram inspecionadas para corrigir o recorte dos ícones
pelo descarte de faces do renderizador 3D.

A revisão final do Flatpak (controle circular de órbita) também foi instalada e
exercitada no modo Trilho 0, com 11.132 objetos e 293 frames, encerramento normal
e captura inspecionada. A janela sem rota foi aberta em uma instância de teste
separada e encerrada pelo identificador dessa instância, sem encerrar janelas
anteriores do usuário. Log: `viewer-icons-track.log` / `viewer-icons-empty.log`.

## Travamento da janela e proporções dos fundos

O relato manual mostrou que o teste anterior de deixar o processo vivo por alguns
segundos não comprovava interatividade. Reproduzido o congelamento na abertura
sem rota: um relatório de pilhas do próprio runtime, obtido na instância de teste,
mostrou a thread principal bloqueada em GraphicsContext.SwapBuffers, no caminho
GLX/Mesa DRI3. O processo não havia fechado; não podia mais tratar entrada ou
redimensionamento.

O viewer agora processa eventos explicitamente no loop, desativa a espera de
VSync nessa janela e limita atualização/renderização a 60 Hz. O limite de render
é restaurado depois do carregamento (o loader original o zerava). A sincronização
do simulador não foi alterada.

`--ui-test` abre sem rota e injeta eventos nativos X11. No teste local, F1 abriu a
ajuda, a janela foi redimensionada para 960×640 e maximizada para 1920×1014, e um
clique nativo no ícone de ajuda continuou funcionando depois da maximização. No
Flatpak instalado, a abertura vazia, entrada nativa, resize e maximização também
passaram com VIEWER_UI_OK e saída zero. A captura maximizada foi inspecionada.

Os fundos arredondados deixaram de usar uma máscara quadrada esticada. São
preenchidos por faixas com raio calculado em pixels da tela, mantendo cantos
circulares em botões, sliders e painéis largos. Ícones e discos preservam suas
texturas próprias. Logs: `viewer-responsive-ui.log`,
`viewer-responsive-flatpak-ui.log`, `viewer-responsive-flatpak-build.log` e
`viewer-responsive-route.log`. O teste da janela congelada foi encerrado pelo
identificador de sua própria instância.

## Route editor e órbita no trilho — 2026-10-19

- `CameraTests`: órbita no trilho preserva o pivô e a posição na via; olhar
  mantém o olho fixo. Salvamento cobre UTF-8 com/sem BOM, UTF-16 e CP1252,
  CRLF, caracteres incompatíveis e recusa de sobrescrita de edições externas.
- `make -C native/linux-launcher check-editor`: a cena X11 ocupa a alocação
  do GtkPaned nos layouts lateral/inferior; editor desancora e reancora com
  histórico de desfazer preservado. Abrir outro arquivo limpa esse histórico.
  Edição pela ação GtkTextView, cancelar, descartar, confirmação de gravação
  e erro de gravação também verificados. Injeção de teclas XTest não é usada
  como evidência: o compositor Wayland pode restringi-la.
- `--editor-test --smoke=9`: cópia descartável da rota Mobitrens Linha 1,
  11.132 objetos e 13 estações. Verifica resize real da cena em cada layout,
  gravação pelo GTK, recarga e preservação da câmera. Saída
  `VIEWER_EDITOR_OK` e `VIEWER_SMOKE_OK`, código zero.
- `--camera-test --reload-test --smoke=6`: órbita pelo globo nos dois modos,
  pivô e âncora preservados, navegação e recarga sem duplicação de objetos.

Evidências em `../build-logs/` do diretório pai do repositório:
`route-editor-document-camera-tests.log`, `route-editor-gtk-tests.log`,
`route-editor-integration.log`, `route-editor-orbit-smoke.log`,
`route-editor-flatpak-build.log` e `route-editor-side.png`.

O Flatpak final também foi instalado localmente e validado com
`--editor-test --camera-test --smoke=8`, usando uma cópia descartável sob
`/home/leonardo/Projetos/OpenBVE/build-logs/route-editor-fixture/` (fora do
armazenamento privado do app). Salvamento, recarga, câmera, layouts e órbita
passaram com saída zero: `VIEWER_EDITOR_OK`, `VIEWER_TRACK_ORBIT_OK`,
`VIEWER_CAMERA_OK` e `VIEWER_SMOKE_OK`. A comparação de dimensões usa a
alocação atual do GTK, pois o gerenciador de janelas pode redimensionar a
janela externa durante a abertura. Logs: `route-editor-flatpak-smoke.log`,
`route-editor-flatpak-gtk-tests.log` e `route-editor-flatpak-install.log`.
Commit Flatpak instalado: `fa5426e4bb252367277d6e1e97ce1b5348cea97c878fe9951de2e5878d8510aa`.

## Ajustes de interface e arrastes — 2026-10-19

O editor passou a reservar faixas próprias para as duas barras de rolagem.
Libadwaita acompanha o esquema claro/escuro do sistema e envia mudanças de tema
aos controles 3D. O card sob a barra superior mostra estação, distância,
horários, lado da plataforma e erros/avisos; mensagens tardias também atualizam
a contagem. Câmera identifica visão livre; olho/F10 alterna a interface e
papel/F8 alterna o editor, sem descartar o buffer.

O arraste mantém o estado durante a transição de foco e acompanha o ponteiro
X11 enquanto o botão está pressionado. A posição do evento de soltura encerra
a interação, evitando que um movimento posterior para outro botão altere a
câmera. O teste nativo envia um evento de movimento antes do botão: OpenTK usa
sua posição de mouse em cache para construir os argumentos do clique.

`check-editor` também verifica barras sem sobreposição, visibilidade do editor,
temas claro/escuro e preservação do texto/histórico. O teste `--feedback-test`
confere eventos nativos da cena, arraste efetivo após pressionar o slider,
órbita em ambos os modos, soltura sem salto, card e os botões de visibilidade.
Evidências: `viewer-feedback-gtk-tests.log`, `viewer-feedback-native.log`,
`viewer-feedback-sandbox-debug.log`, `viewer-feedback-flatpak-build.log`,
`viewer-feedback-flatpak-test.log` e `viewer-feedback-preview.png`, no diretório
`build-logs` do diretório pai do repositório.

A instalação Flatpak final passou por `--feedback-test --smoke=11`, com
`VIEWER_FEEDBACK_OK`, `VIEWER_SMOKE_OK` e saída zero. A prévia foi capturada da
janela GTK dessa instalação após restaurar o tema do sistema. Commit instalado:
`d1f5fb69dc5c6d6f5172db287c0509da60b58535324c0f125232041ec0f1bf0c`.

## Globo gira na posição da câmera — 2026-10-19

O globo e o arraste central giram a direção da visão preservando a posição.
No modo Trilho 0, mantêm os deslocamentos locais e a posição da cabine na via;
no modo Livre, giram a partir da posição livre atual, sem pivô externo.
A translação da órbita anterior e o estado de pivô da cabine foram removidos.

Os testes de câmera verificam a preservação da posição/offset da cabine.
`--feedback-test --camera-test --smoke=11` usa o caminho de entrada nativa:
confere mudança da direção com o olho fixo no trilho e numa posição livre
explicitamente afastada da via, inclusive após soltar o globo.
Logs: `viewer-globe-position-tests.log`, `viewer-globe-native-test.log`,
`viewer-globe-build.log` e `viewer-globe-flatpak-test.log`, em `../build-logs/`
a partir da raiz do repositório.

Atualização instalada e validada também no Flatpak, com os mesmos argumentos,
`VIEWER_TRACK_ORBIT_OK`, `VIEWER_FEEDBACK_OK`, `VIEWER_SMOKE_OK` e saída zero.
Commit instalado: `5a0a52928c15c2dd80a7e81739af52d67a0908a9dbe9e85e7bc693342c2451f3`.

## Serviços de IA e circulação — 22/10/2026

- Repetição e múltiplas instâncias por XML, com frota limitada e reutilizável.
- Serviços encadeados, troca contínua nos terminais, distribuição inicial
  balanceada e preferência por espera na estação antes do trecho ocupado.
- Separação em movimento e reservas conjuntas de garganta/plataforma para
  GAT e CAC, sem bloquear o jogador estacionado na via paralela de CAC.
- 93 verificações de tráfego aprovadas em Release, incluindo quatro horas
  simuladas com os roteiros versionados e cenários de bloqueio pelo jogador.
- A rota noturna versionada registra L1_GAT, L1_CAC, L2_PTO e L2_ROD;
  o modelo 9000 é referenciado sem duplicar seus arquivos.
- O empacotamento executa RepeatTests junto dos testes de câmera, gráficos,
  texturas, documentos e launcher. O instalador fixa a release
  linux-native-2026.10.22.1.

Os testes de circulação usam geometria reta de teste e dispensam o renderer
na troca de representações. Não substituem a inspeção visual das manobras no
mapa real nem implementam proteção automática do trem do jogador.

O bundle desta versão foi instalado sobre o Flatpak existente no escopo do
usuário (commit a83f111aa79a2941a734ab0f5942480b0d7a64c1bcc38c7c9bd882d5ba4a04ad).
O motor dentro do pacote carregou a rota noturna versionada, renderizou 477
quadros e encerrou com NATIVE_SMOKE_OK. A captura foi inspecionada; o conteúdo
mantém avisos de recursos ausentes e a cortina inicial da cabine do trem de
teste, portanto essa carga não é uma validação de ausência de avisos visuais.
