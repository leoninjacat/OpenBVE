# Como configurar terminais de ida e volta

O próprio simulador executa a operação. Não é necessário um agente de IA,
Ctrl+A ou uma conexão com o ChatGPT para contar o tempo e preparar a volta.

A rota tem dois arquivos com o mesmo nome:

- `idavolta.csv`: trilhos, estações, objetos e AMVs.
- `idavolta.roundtrip.xml`: sequência de paradas e operação dos terminais.

No XML, `Outbound` representa a ida e `Return`, a volta. A última parada
de cada bloco é o terminal de chegada. Depois da parada correta, abertura
das portas, espera e fechamento, o simulador posiciona o trem na primeira
parada do outro bloco. O relógio continua e o próximo destino é ativado.

## Exemplo do Terminal Norte

No final de `Outbound`:

```xml
<Stop station="2" rail="0" position="2490" dwell="30" doors="L" />
```

Na primeira parada de `Return`:

```xml
<Stop station="2" rail="1" position="2310" dwell="0" doors="L" />
```

Isso significa: chegar à estação 2 pela via 0, parar em 2490 m, abrir as
portas à esquerda e aguardar 30 segundos. Depois, fechar as portas e
preparar a partida pela via 1, com a frente do trem em 2310 m, voltada
para a quilometragem decrescente. Se outra composição ocupar o espaço
de destino, o reposicionamento aguarda a liberação.

| Campo | Significado |
|---|---|
| `station` | Índice da estação no CSV, começando por 0, na ordem das declarações de estação. |
| `rail` | Número da via em que o trem deve parar ou ser posicionado. |
| `position` | Posição da extremidade dianteira do trem no sentido daquele serviço, em metros. |
| `dwell` | Tempo de espera, em segundos, após abrir as portas. Na chegada ao terminal, é o contador da reversão. |
| `doors` | L = esquerda; R = direita, vistas da cabine ativa. |

O Terminal Sul usa a mesma regra: última parada de `Return`, seguida da
primeira parada de `Outbound`. Basta editar o XML completo que acompanha
a rota para alterar tempos, vias e pontos de parada. Estes exemplos são
trechos explicativos, não substituem o XML inteiro.

## Cuidados com o traçado

As vias precisam existir no trecho em que toda a composição será colocada.
Reserve plataforma e trilhos para o comprimento do trem; as posições de
ida e volta normalmente são diferentes porque a frente muda de extremidade.
O plano não cria uma conexão física: os AMVs e os conectores continuam
sendo definidos no CSV. O teste usa os comandos nativos `Track.Switch` e
`Track.SwitchT`.

As entradas `Throat` do XML delimitam os conjuntos de AMVs que ficam
travados enquanto uma composição ocupa a região. Por exemplo:

```xml
<Throat start="600" end="750" rails="0,1,2" />
```

O jogador continua responsável pela condução. A automação do terminal e
o piloto automático de Ctrl+A são funções diferentes; o piloto ainda
precisa ser adaptado ao plano de ida e volta.
