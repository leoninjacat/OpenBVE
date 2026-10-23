# Primeira viagem Linux nativa (.NET 10)

O executável nesta pasta usa o motor real do OpenBVE em .NET 10, sem iniciar
Mono ou carregar WinForms/GDI+. O launcher GTK continua sendo um processo
separado. Esta é uma versão de desenvolvimento, ainda sem paridade completa.

## Instalar como aplicativo Linux

O empacotamento Flatpak inclui o runtime .NET e integra o launcher ao menu de
aplicativos. Veja [as instruções de instalação e geração](../packaging/README.md).
O port continua experimental e mantém as limitações de formatos descritas abaixo.

## Compilar e executar

Requisitos: SDK .NET 10, OpenGL/driver funcional, X11/XWayland, OpenAL e fontes
instaladas com `fontconfig` (`fc-match`). SDL2 é necessário se esse backend for
selecionado. O primeiro build restaura os pacotes NuGet.

Na raiz do repositório:

```bash
native/dotnet10/build.sh
make -C native/linux-launcher
native/linux-launcher/build/openbve-launcher
```

Feche uma instância antiga do launcher antes de reabrir: uma janela já aberta
continua executando o código anterior. O binário antigo diretamente dentro de
`native/linux-launcher/` foi preservado; use o de `build/` acima.

Selecione a rota e a pasta do trem e clique em **Iniciar viagem**. O botão agora
inicia `native/dotnet10/runtime/OpenBve`, passando cada caminho como um argumento,
sem shell. Fica desabilitado enquanto o processo estiver ativo e volta ao encerrar.
`OPENBVE_NATIVE_ENGINE` permite indicar outro executável para desenvolvimento.

Também é possível executar o motor diretamente:

```bash
native/dotnet10/runtime/OpenBve '/route=/caminho/rota.csv' '/train=/caminho/trem'
```

O dropdown **Modo de jogo** oferece **Normal** e **Modo CCO (Beta)**.
No CCO, o trem começa com piloto automático e Ctrl+A não o desativa.
Numpad 1/7 selecionam a câmera da estação anterior/seguinte, em ciclo.
Cada estação mantém seus ajustes de câmera durante a sessão. **Numpad 5** restaura
a posição, orientação e zoom iniciais da câmera da estação selecionada, mesmo
quando o trem está em outra estação. O nome da estação observada permanece
visível no canto superior direito. **Pular para estação**
seleciona a câmera sem mover o trem ou adiantar o relógio. As câmeras iniciais são
geradas ao lado da via principal, usando o ponto de parada de cada estação.
**F7** alterna as visualizações do CCO: **mapa com curvas → diagrama simplificado
→ oculto → mapa com curvas**. No modo CCO, a sessão começa pelo mapa; **no modo normal, começa oculto e F7
abre o mapa sem mudar a câmera, o piloto automático, o relógio ou o tráfego IA**.
A estação destacada no modo normal é a do trem do jogador. No estado oculto,
os painéis e o nome da estação somem; as câmeras e o piloto automático continuam
ativos. Segurar F7 não percorre vários estados.

O diagrama semitransparente maior mostra as vias 0 e 1 em paralelo,
estações numeradas e nomes ao lado. A estação 1 fica embaixo; o espaçamento entre
estações é uniforme e a posição dos trens é interpolada entre suas paradas.
Os indicadores têm corpo retangular e ponta no sentido de circulação: azul para
IA e verde para o trem condutor em movimento. Qualquer trem parado fica vermelho
(tolerância de 0,01 m/s); ao retomar o movimento, recupera sua cor. Os serviços XML mantêm o sentido planejado
mesmo nas paradas. Sem sentido definido, o indicador fica retangular.
O mapa com as curvas é exibido separadamente, mantendo a proporção do
traçado. As duas visões destacam em amarelo a estação da câmera selecionada e mostram
apenas trens ativos nas vias 0 e 1, inclusive em Central Manoel Lacerda. Em
Cachorros, a ligação pelas vias 4 e 50 também fica visível, representada como
continuação da via 1 no diagrama e pela geometria real no mapa. A exceção se
limita ao trecho do terminal: via 4 desde 540 m antes da parada, via 50 desde
415 m antes, até 35 m após a parada. Trens nas demais vias (como os da Linha 2)
e nas vias 4/50 fora desse trecho continuam filtrados.

### Salto manual para estação

No modo normal, **Pular para estação** move apenas o trem do jogador, mantendo o
relógio, as posições e o andamento dos serviços de IA. O menu não reinicializa
mais os trens de tráfego. A parada de destino começa um novo tempo de embarque
no horário atual, sem obrigar a esperar o horário futuro da tabela; os avisos
CCO continuam aguardando o fim desse tempo de parada. A fila de áudio anterior
do jogador é limpa, sem desligar o rádio.

No modo CCO, a mesma opção continua selecionando apenas a câmera da estação.
Saltos automáticos definidos pela rota conservam seu comportamento original.
Para testar o salto manual, use `/mode=normal /native-jump-test /native-smoke=6`
com a rota Mobitrens Linha 1. O teste salta para Manoel Lacerda e retorna duas
vezes, verificando relógio, todos os eixos, estados e temporizadores dos trens
IA, a frota e o novo tempo de parada. O sucesso imprime `STATION_JUMP_OK`.

### Rádio CCO e anúncios na cabine

O trem do jogador reproduz o anúncio da estação ao iniciar a parada para embarque,
na posição do condutor, uma vez por visita. Na Mobitrens, os arquivos opcionais são
`Railway/Sound/Mobitrens/Anuncios/announce_*.wav`, os mesmos usados pelos serviços IA.
Em Gatos, a cabine usa `destino_cachorros.wav`, em vez de `announce_gatos.wav`.
Os avisos do jogador são enfileirados para não se sobreporem entre si.

O rádio CCO funciona também no modo normal, sem ativar piloto automático nem mudar
a câmera. **Ctrl+F7** liga/desliga somente o rádio; os anúncios de estação continuam.
O rádio começa ligado quando os dois WAVs existem na pasta de conteúdo da rota:
`Railway/Sound/Mobitrens/CCO/aguarde.wav` e `liberado.wav`.
**Os avisos do CCO só começam depois de terminar o tempo de parada**, e esperam
qualquer anúncio da estação que ainda esteja tocando.

Enquanto o jogador permanece parado na estação, o CCO observa trens ativos no
mesmo trilho, entre o jogador e a próxima estação. Uma ocupação dispara o aviso de
espera uma vez. A liberação requer um segundo de situação livre estável; sinal
fechado/desconhecido impede o aviso de liberação. Esses avisos são orientações:
não alteram sinais, comandos, reservas de tráfego nem o horário de partida.
O rádio não calcula um novo itinerário através de AMVs; sua observação de tráfego
usa o trilho atual e a sinalização carregada da rota.

Para verificar o rádio de forma reproduzível, acrescente `/native-cabin-test
/native-smoke=6` ao comando do motor nativo, usando a rota Mobitrens Linha 1 com
os áudios instalados. Esse teste temporário verifica um trem no ponto de parada
da próxima estação na mesma via: nenhum aviso antes do fim da parada e áudio
de espera depois. Também verifica que trens na outra via não bloqueiam, que
sinal fechado/desconhecido não produz liberação e que a cabine em Gatos usa
`destino_cachorros.wav`. O cenário é restaurado antes de continuar a simulação;
o processo imprime `CABIN_TEST_OK` e encerra após seis segundos de execução.

Arquivos ausentes são opcionais e não causam erro em outras rotas. O atalho informa
quando os WAVs do rádio não estão disponíveis. Os áudios seguem ligados ao carro do
condutor e acompanham o trem. A fila é limpa ao trocar de estação, pular de local
ou recuar o relógio; nenhum histórico de estações é reproduzido.

Pela linha de comando, use `/mode=cco` (padrão: `/mode=normal`).

`/ai=true` ativa o piloto automático. `/width=1280 /height=720` ajusta a janela.
`--help` mostra o uso. As configurações ficam em `~/.config/OpenBVE-Native/Settings`,
isoladas da instalação Mono (ou sob `$XDG_CONFIG_HOME`). O log fica nessa pasta,
em `log.txt`. No Flatpak, os dados ficam na pasta privada descrita no guia de instalação.
O build monta os arquivos em staging e só atualiza runtime após todos compilarem;
não remove as configurações de viagens anteriores. Não execute build durante uma
viagem, pois as bibliotecas do runtime serão atualizadas.

Controles herdados de `assets/Controls/Default.controls`: F/V movem o reversor;
Z/A/Q controlam potência/neutro/freio nos trens de manípulo único; 1 aplica a
emergência. Para controles separados, Z/A ajustam potência e vírgula/ponto
ajustam freio. F5/F6 controlam portas; F1/F2 alternam cabine/exterior. As novas
câmeras propostas para os viewers ainda não fazem parte desta etapa.

No trem local Série 1000 Cofebra, a cortina fica fechada com reversor neutro.
Isso é definido em `panel.animated` pelo próprio conteúdo; não é cenário ausente.

## Implementação e reaproveitamento

- Projetos SDK em diretórios separados compilam os arquivos de `source/` por
  referência. Arquivos em `Native/` substituem pontos que dependiam do desktop
  antigo. O código original em `source/` não foi alterado.
- LibRender2, SoundManager, RouteManager2, TrainManager e o loop da simulação
  continuam sendo os originais. O entrypoint preserva a inicialização do motor,
  usa a seleção por argumentos e encaminha os erros de diálogo ao stderr/log.
- NativeGraphics implementa com Skia o subconjunto de desenho usado por fontes,
  cursores, mapas e timetable. Em builds Skia sem gerenciador de fontes, resolve
  o arquivo da fonte via `fc-match`; os argumentos não passam por shell.
- Plugins compilados: Route.CsvRw, Train.OpenBve, Object.CsvB3d, Object.Animated,
  Sound.RiffWave, OpenBveAts e Texture.Native. Formats.Msts/Formats.OpenBve são
  bibliotecas de suporte, sem implicar suporte completo ao conteúdo MSTS.
- Texture.Native reaproveita os decodificadores gerenciados BMP/PNG/GIF originais;
  o fallback de imagens usa SkiaDecoder. O uso de ImageSharp foi removido para
  permitir builds Release sem a exigência de uma chave de licença dessa biblioteca.
- O carregamento de plugins usa Assembly.LoadFrom para compartilhar a API no
  contexto padrão. Dependências de plugins são resolvidas ao lado do executável.
- Bibliotecas nativas do OpenTK são resolvidas explicitamente para os nomes Linux;
  não dependem do mecanismo dllmap do Mono.
- Restaurado o construtor Texture(Texture) para registro de dados brutos. Corrigida
  a conversão Skia de BGRA/premultiplicado para RGBA não premultiplicado usando
  ReadPixels: CopyTo podia recriar o destino no formato da origem.
- Corrigidos os nomes dos recursos de controles/cursores para o namespace OpenBve.

## Testes

```bash
dotnet run --project native/dotnet10/NativeTests
make -C native/linux-launcher check check-ui
```

NativeTests verifica ordem dos canais, alpha, alinhamento BGR, origem de textura,
registro de textura bruta, medição de fonte e decodificação PNG. O teste GTK exige
um display; verifica validação, argumentos com espaços/metacaracteres, encerramento
normal e falha do processo usando um executável temporário.

Para repetir o teste completo em uma sessão gráfica, use conteúdo instalado:

```bash
native/dotnet10/runtime/OpenBve '/route=/caminho/rota.csv' '/train=/caminho/trem' \
  /ai=true /native-smoke=32 /native-capture-after=28 \
  /native-capture=/caminho/captura.png
```

O teste só começa a contar após a simulação estar pronta. Captura o framebuffer do
próprio jogo e encerra após a duração informada, registrando frames, velocidade e
posição. Não altera controles nem física. Um timeout externo deve ser usado para
limitar falhas de carregamento; timeout sem NATIVE_SMOKE_OK não confirma sucesso.

Validação realizada com a rota local `Mobitrens Linha 1 - Gatos-Cachorros.csv` e a
pasta `Serie 1000 Cofebra`, .NET 10.0.12 / SDK 10.0.112. Carregamento, renderização,
plugin ATS padrão e física com piloto automático foram exercitados; um teste de
32 segundos registrou 1.916 frames e 8,608 m/s (~31 km/h). A cabine e a rota foram
verificadas em uma captura real. O conteúdo instalado não é redistribuído aqui.

## Limitações

- O pacote Linux ainda é experimental: outros plugins de objeto/rota/som não estão
  incluídos. Trens com DLLs ATS Win32 e proxy WCF não são suportados no Linux.
- TIFF não é decodificado pelo fallback Skia. BMP, PNG, GIF e JPEG são suportados.
- O fallback de WAV que dependia do Windows ACM informa codec não suportado;
  os leitores gerenciados existentes continuam disponíveis. Sons desse fallback
  precisam ser convertidos para PCM WAV.
- Algumas texturas do pacote local têm cabeçalhos zerados e alguns arquivos estão
  ausentes; o log registra essas falhas. Não foram modificados nem substituídos.
- Fontes e desenho Skia podem diferir nas métricas e na disposição. Mapas,
  timetable, todos os alfabetos, cursores especiais, dispositivos de entrada,
  todas as teclas e modos de janela ainda não foram validados visualmente.
- OpenAL inicializou no teste; a fidelidade auditiva não foi verificada por escuta.
- As prévias simplificadas CSV/RW do launcher permanecem separadas. Integrá-las ao
  parser nativo recém-compilado é trabalho posterior.
- O build ainda emite avisos herdados de plataforma, APIs obsoletas, leituras de
  stream do código original.

Backups e arquivos existentes da migração foram preservados. O empacotamento acrescenta a publicação self-contained, configuração em pasta
gravável e decodificação de fallback com Skia. Nenhum commit foi criado automaticamente.

## Route Viewer nativo

O visualizador de rotas tem um projeto .NET 10 separado, câmera livre e controles
na tela. Veja [RouteViewer/README.md](RouteViewer/README.md). O instalador Flatpak
inclui os dois aplicativos e suas entradas no menu.
