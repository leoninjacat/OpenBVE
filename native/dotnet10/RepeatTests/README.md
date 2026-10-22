# Repetição experimental de TFOs

> Configuração atual e proteção de tráfego: veja [FLUID-TRAFFIC.md](FLUID-TRAFFIC.md).
> Os exemplos abaixo documentam as etapas anteriores do protótipo.


Em `Definition`, `RepeatInterval` é um número de segundos entre aparecimentos
reais. Zero ou ausência mantém o comportamento anterior. Por exemplo:

```xml
<AppearanceTime>00.0000</AppearanceTime>
<LeaveTime>00.0300</LeaveTime>
<RepeatInterval>240</RepeatInterval>
```

A composição desaparece após 180 segundos, é reposicionada enquanto invisível
e reaparece 240 segundos depois do aparecimento anterior. O modelo e o plano
são carregados uma única vez. Não existe limite de repetições.

Um segundo XML começando às 00.0200 e repetindo a cada 240 segundos alterna
os sentidos a cada dois minutos. Cada XML mantém uma composição reutilizável;
por isso LeaveTime deve ser positivo e menor que RepeatInterval. Valores
inválidos desativam a repetição e geram erro de carregamento. Não se deve usar
RunInterval: esse campo já tem outra função para os trens precedentes.

Condições de posição continuam valendo. Se atrasarem uma aparição, o próximo
intervalo conta da aparição efetiva; não há sincronização global dos sentidos
nem proteção de ocupação/sinalização neste protótipo. Após saltos de tempo,
partidas perdidas são descartadas. Os horários de chegada também incluem
aproximação e abertura/fechamento das portas.

A implementação está nos fontes compartilhados de TrainManager, no parser
do executável e no enum de Formats.OpenBve. Compile e abra o runtime atualizado;
um processo já aberto continua usando o código anterior.

Teste sem interface:

```sh
dotnet run --project native/dotnet10/RepeatTests/RepeatTests.csproj
```

O teste verifica reuso, relógio da IA, descarte de partidas perdidas e
compatibilidade de descarte sem repetição. A geometria e animação precisam
ser observadas no simulador.
