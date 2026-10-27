# Tráfego de IA da rota noturna

Abra `../Mobitrens Linha 1 (GAT-CAC) - night.csv` com o OpenBVE Native atualizado.
Os quatro XMLs usam o modelo 9000 já existente em `../../Trens_XML/9000`.

- Linha 1: L1_GAT e L1_CAC, oito composições em ciclo e intervalo mínimo de 6min30.
- Linha 2: L2_PTO no rail 2 e L2_ROD no rail 3, quatro minutos por sentido,
  com defasagem inicial de dois minutos.
- A Linha 1 tem 23 minutos nominais por percurso e três minutos de retorno
  em cada terminal. Ocupação e manobras podem ampliar esses tempos.
- A IA prefere aguardar na estação anterior, mantém separação durante o
  percurso e reserva as manobras de Gatos e Cachorros.

A frota inicia distribuída pela linha. O jogador continua responsável por sua
condução: a proteção dos TFOs não substitui a sinalização do trem do jogador.
Estas extensões XML exigem este motor atualizado; não são comandos do OpenBVE
original. Detalhes e testes em `native/dotnet10/RepeatTests/FLUID-TRAFFIC.md`
na raiz do repositório.
