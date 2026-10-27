# Paradas por via — OpenBVE by LT ARTS

Extensão local do CSV (o quinto argumento é novo neste fork):

```csv
.Stop ladoDaPlaca;toleranciaAtras;toleranciaAdiante;numeroDeCarros;via
```

Exemplo de parada na via 2, para qualquer composição:

```csv
1250,.Sta Estacao Expressa;;;0;R;0;0;;20;100
1450,.Stop 1;5;5;0;2
```

A estação precisa ser declarada com `.Sta` antes de seu `.Stop`. O primeiro argumento de `.Stop` continua sendo o lado da placa, e não o lado das portas. As portas são configuradas em `.Sta`. A via deve existir nessa posição. O alinhamento dos AMVs determina se o trem chega à via declarada.

Omitir o quinto argumento mantém uma parada sem restrição de via, como nos CSVs legados. Para estações exclusivas de um ramal, declare a via explicitamente. Eventos de estação passam a existir também nas vias secundárias válidas, e o piloto tradicional encontra as paradas nessas vias. A câmera do editor é independente do itinerário.

No modo de ida e volta, o piloto usa `rail` e `position` de cada Stop no arquivo `.roundtrip.xml`, calcula a distância no sentido da perna atual, aguarda o serviço de portas e continua depois da reversão. Este trabalho não acrescenta etapas de manobra contínua aos terminais: a reversão do modo atual continua usando reposicionamento.

Fixtures de validação em Railway/Route/Mobitrens/IdaVolta:
- paradas-por-via-teste.csv: piloto tradicional, via 0 → 1 → 0, parada restrita na via 1.
- paradas-ida-volta-teste.csv e XML: piloto com itinerário bidirecional.
