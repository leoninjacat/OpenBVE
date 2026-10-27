# Várias composições por XML

> Configuração atual e proteção de tráfego: veja [FLUID-TRAFFIC.md](FLUID-TRAFFIC.md).
> Os exemplos abaixo documentam as etapas anteriores do protótipo.


Configure em Definition:

```xml
<SpawnInterval>240</SpawnInterval>
<FleetSize>13</FleetSize>
<BirthClearance>50</BirthClearance>
```

SpawnInterval é o intervalo mínimo em segundos entre introduções efetivas do
mesmo serviço. FleetSize limita o número de instâncias independentes por XML
(1 a 32). BirthClearance é a margem em metros ao redor do corpo da composição
no nascimento. O CSV continua com apenas uma chamada Route.TfoXML por serviço.

As instâncias são pré-carregadas a partir do mesmo arquivo durante a carga da
rota, cada uma com seu próprio trem, carros, portas, sons e plano de movimento.
São reutilizadas indefinidamente. Não há leitura de modelos no loop de frames
nem crescimento ilimitado de memória. Mais instâncias custam memória e tempo
de carregamento, mesmo enquanto invisíveis.

O despachante prioriza a solicitação mais antiga, com desempate por slot.
A próxima introdução fica disponível somente SpawnInterval segundos após a
partida efetiva. Bloqueios e saltos de relógio não geram partidas acumuladas.
Uma frota pequena pode esgotar as instâncias livres e ampliar o intervalo.

Com NextService, cada slot mantém seu próprio par no serviço sucessor. Ambos
os XMLs precisam declarar o mesmo FleetSize e identidades únicas por serviço.
TriggeredStart=true deixa todos os slots do retorno aguardando seus pares.
Uma viagem concluída libera apenas o retorno daquele slot, não a frota inteira.
Os intervalos de cada sentido são independentes; um retorno pode aguardar a
liberação de seu nascimento mesmo após a conclusão do percurso anterior.

Sem NextService, LeaveTime retira uma instância e a devolve à fila da frota.
Use um LeaveTime positivo nesse modo. SpawnInterval substitui RepeatInterval;
o RepeatInterval antigo continua funcionando nos XMLs que não usam frota.

Antes da introdução, o teste de ocupação consulta os trens normais (incluindo
o jogador) e TFOs disponíveis, considerando o comprimento completo dos carros
na mesma via e os eixos ainda em transição. Instâncias ocultas são ignoradas.
A verificação não detecta cruzamentos geométricos entre índices de via diferentes
nem protege a composição durante o percurso. Não equivale a sinalização/CCO.

Mobitrens, rota noturna:
- L1_GAT e L1_CAC: 13 slots pareados, intervalo mínimo de quatro minutos por
  sentido; 23 minutos de percurso por JourneyTime e três minutos finais em CAC.
  A frota representa até 13 composições visíveis, com duas representações
  pré-carregadas por composição para os dois percursos. Introduções aguardam
  quando o terminal está ocupado, portanto horários podem atrasar.
- L2_PTO e L2_ROD: dois slots por sentido; quatro minutos, defasagem inicial de
  dois minutos e duração de três minutos por aparição.

Use o launcher de desenvolvimento e o runtime reconstruído; o Flatpak do menu
não é atualizado pelo build local. Falta validação visual da frota completa.

Verificação: dotnet run --project native/dotnet10/RepeatTests/RepeatTests.csproj
Inclui frota simultânea, fila, bloqueios, salto de tempo, corpo dos carros,
transições de via, pares independentes e duas horas de operação em tempo simulado.
