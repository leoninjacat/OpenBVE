# Serviços encadeados da Linha 1

> Configuração atual e proteção de tráfego: veja [FLUID-TRAFFIC.md](FLUID-TRAFFIC.md).
> Os exemplos abaixo documentam as etapas anteriores do protótipo.


A rota noturna Mobitrens registra dois percursos: L1_GAT.XML e L1_CAC.XML.
Os antigos 1.XML, 2.XML e 3.XML estão preservados, mas desativados nessa rota.
Linha 2 permanece independente com L2_PTO.XML e L2_ROD.XML.

Extensão experimental na seção Definition:

- ServiceId: identificador único do serviço.
- NextService: identificador do próximo serviço, ativado após a conclusão real
  do roteiro e a espera final. Deve corresponder a exatamente um TFO inativo.
- TriggeredStart: true mantém o serviço oculto até ser ativado pelo anterior.
- JourneyTime: duração da viagem no formato hh.mmss. Ajusta proporcionalmente
  a linha de tempo inteira do roteiro, inclusive velocidades efetivas e esperas
  intermediárias. Zero ou ausência usa a duração calculada a partir do roteiro.
- CompletionWait: espera real no destino no formato hh.mmss, fora da escala
  de JourneyTime. O StopTime final deve comportar a abertura das portas durante
  essa espera. A troca usa a chegada, não o DepartureTime final, como referência.

NextService e RepeatInterval não são usados juntos. Nos serviços encadeados,
LeaveTime não força o desaparecimento: o próprio término da viagem o faz.
A ativação respeita as condições de posição de cada XML. Não há controle de
ocupação nem proteção por sinais. A busca do serviço sucessor ocorre no
conjunto de TFOs da rota atual, sem registros estáticos entre carregamentos.

L1_GAT: CAC, 15990 m, rail 50; 15575 → rail 4; 15450 → rail 1;
paradas até Gatos; destino 200 m no rail 1. Ativo desde 00:00.
L1_CAC: 200 m no rail 0; paradas até Smallville; 15575 → rail 50;
Cachorros em 15990 m; espera 180 segundos antes do retorno.

Ambos usam JourneyTime=00.2300. Sequência nominal:
00:00 CAC→GAT; 00:23 GAT→CAC; 00:46 chegada em CAC;
00:49 CAC→GAT; 01:12 GAT→CAC; repetição ilimitada.
Pode haver diferença de frames pela ordem de atualização e pela introdução
inicial dos trens. O trem do jogador mantém sua própria duração e movimento.

Testes: dotnet run --project native/dotnet10/RepeatTests/RepeatTests.csproj
Incluem dois ciclos de encadeamento e a espera final, além da repetição da L2.
É necessário reconstruir e usar o runtime de desenvolvimento; o Flatpak do
menu não é atualizado por native/dotnet10/build.sh.

A configuração ativa agora usa várias instâncias por XML; consulte FLEETS.md.
Os horários acima descrevem uma composição isolada. Com frota, as introduções
respeitam também o intervalo compartilhado e a ocupação do nascimento.
