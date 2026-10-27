# Operação contínua e proteção local de tráfego

Configuração ativa da Linha 1: dois XMLs, oito pares de representações e intervalo
mínimo de 390 segundos (6min30) por sentido. Cada percurso tem JourneyTime de 23 minutos,
mais CompletionWait de três minutos em ambos os terminais. Intervalos podem
crescer por ocupação e por manobras; não são uma grade com partidas forçadas.
Os três minutos são espera mínima no destino, não um intervalo de nascimento.

Novas opções em Definition:

```xml
<ContinuousHandoff>true</ContinuousHandoff>
<ProtectedTraffic>true</ProtectedTraffic>
<WarmStart>true</WarmStart>
<ConflictZones>GAT:0:350:0,1;CAC:15350:15775:0;CAC:15350:16025:1,4,50</ConflictZones>
```

WarmStart é ativado somente no serviço inicial L1_GAT. Antes da primeira cena,
o motor distribui a frota ao longo dos dois percursos. Não insere uma composição
nova na plataforma a cada intervalo. Aproximações iniciais são recuadas para
fora das gargantas, evitando começar com uma chegada disputando uma saída.
Há um período transitório de regularização; as partidas seguintes vêm das
chegadas efetivas e do tempo de retorno, com reutilização dos mesmos pares.

ContinuousHandoff mantém o predecessor visível, parado no destino, enquanto
o sucessor aguarda intervalo e ocupação. Ao liberar, troca as representações
na mesma atualização. Os pontos finais/iniciais precisam coincidir em posição,
via e formação. Em GAT, L1_GAT passa do rail 1 ao rail 0 em 225 m e termina em
200 m, coincidindo com o início de L1_CAC. Em CAC, ambos usam 15990 m no rail 50.
O StopTime final em CAC é 2:50, deixando margem para fechar as portas antes
da troca após três minutos reais de CompletionWait.

ProtectedTraffic verifica separação em movimento, usando o corpo inteiro dos
carros e margem de BirthClearance mais uma distância conservadora de frenagem.
O roteiro é pausado durante bloqueios, sem teletransporte para recuperar o
horário. A detecção olha também a via planejada na posição do obstáculo, para
não bloquear uma chegada que trocará para o rail 50 por causa de um jogador
estacionado mais adiante no rail 0.

ConflictZones usa [grupo:]início:fim:rails, separados por ponto e vírgula.
Partes com o mesmo grupo compartilham uma única reserva, obtida atomicamente.
Em CAC, isso reserva a garganta na via 0 junto com o percurso 1/4/50 e a
plataforma: a chegada espera antes da garganta se houver trem no rail 50.
A via 0 na plataforma de CAC fica fora dessa reserva, permitindo ao jogador
estacionar ali. A reserva só é liberada quando toda a composição sai do percurso.
Não se adquire a garganta sem verificar a plataforma, evitando bloqueio mútuo.

A Linha 2 mantém intervalo de 240 segundos por sentido, com defasagem inicial
de dois minutos. ProtectedTraffic verifica separação na mesma via; suas vias
2 e 3 não participam dos grupos de manobra da Linha 1.

Limitações: proteção local configurada para esses percursos, sem integrar a
sinalização completa. O jogador não é freado pelo controlador. Bloqueios fazem
a IA parar conservadoramente; frenagem progressiva para tráfego ainda precisa
ser refinada. Cruzamentos de outras vias precisam de ConflictZones explícitas.
A aparência e animação no mapa real ainda requerem inspeção visual.

Verificação sem interface:

```sh
dotnet run --project native/dotnet10/RepeatTests/RepeatTests.csproj -- \
 '/caminho/Mobitrens/Linha 1/Trens_XML'
```

Além dos testes unitários, executa os roteiros reais dos XMLs durante duas
horas simuladas com oito carros por composição, em geometria de teste reta,
reservas de CAC/GAT e um trem do jogador estacionado em CAC. Repete o cenário
com o jogador bloqueando GAT por dez minutos antes de liberar e estacionar em
CAC. Verifica ausência de sobreposição na mesma via, exclusão mútua de CAC,
reutilização de frota e continuidade de circulação sem bloqueio permanente.
O teste dispensa apenas o renderer durante a troca de representações.

Use o launcher/runtime de desenvolvimento recompilado, não o Flatpak do menu.

## Preferência por espera na estação

As Linhas 1 e 2 ativam `<PreferStationWaiting>true</PreferStationWaiting>`
em Definition, junto de ProtectedTraffic. Antes de fechar as portas de uma
parada, o controlador verifica todo o percurso até o próximo Stop, incluindo
pontos de troca de via, a plataforma de destino e reservas de ConflictZones.
Se houver ocupação, mantém a composição no último instante da parada com portas
abertas. Quando libera, segue a sequência normal de fechamento e aceleração.
O tempo de espera extra não provoca recuperação de posição por salto.

Em Smallville, uma ocupação em Smallville→CAC ou na plataforma do rail 50
prolonga a parada em Smallville. O jogador estacionado na via 0 de CAC é tratado
como via paralela após a troca para 50. As zonas de manobra necessárias são
reservadas antes de deixar a estação, impedindo uma IA concorrente de tomar
a garganta enquanto o trem se aproxima.

O teste é uma verificação de ocupação atual, não uma previsão de horários ou
controle do jogador. Se surgir um conflito depois da partida, a proteção em
movimento continua podendo parar o trem na linha. Composições do tráfego inicial
que já começam entre estações também usam essa proteção até sua próxima parada.
A opção ausente/false preserva o comportamento anterior.

A suíte verifica espera nos dois sentidos, portas abertas, liberação sem salto,
JourneyTime escalado, reserva antecipada da garganta, troca 0→50 e proteção a
um conflito surgido após a partida. Os cenários completos dos XMLs também
verificam que a ocupação de CAC gera espera na estação de Smallville.

## Frota balanceada

O ciclo nominal é 52 minutos: 23 de viagem e três de espera em cada sentido.
Oito composições e 390 segundos dividem esse ciclo exatamente. A distribuição
inicial usa `slot * ciclo / quantidade`, incluindo o intervalo entre a última
e a primeira composição. O intervalo de despacho continua sendo um mínimo,
podendo aumentar por bloqueios, manobras e atrasos.

Quando um trem inicial ficaria na região de uma manobra, ele é recuado até
uma parada real anterior do roteiro, em vez de simplesmente perder alguns
segundos de percurso e ficar entre estações. A espera mínima nos terminais
permanece de três minutos. A Linha 2 não teve frota ou intervalo alterados.

Uma troca de serviço com predecessor visível tem prioridade sobre um nascimento
inicial atrasado. Isso evita a espera circular em que um trem oculto aguarda a
plataforma enquanto o trem dessa plataforma depende de um retorno preso atrás
dele na fila. Sementes iniciais que aguardam mais de um segundo perdem a isenção
de intervalo da carga inicial, evitando uma partida adicional colada à anterior.

No teste de duas horas com jogador estacionado em CAC, o tempo agregado de
espera em Smallville caiu de 4553 para 724 segundos. O cenário com GAT bloqueado
por dez minutos caiu de 3492 para 436 segundos. São medidas da geometria de teste,
não uma garantia de tempo ou comportamento visual no mapa real.
