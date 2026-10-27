# Assistente de trens de IA (Windows)

Abra `Instalar-Trens-IA.cmd` ou `OpenBve.TrafficInstaller.exe` na pasta
`native/dotnet10/openbve-windows-beta`. É um aplicativo separado: o launcher do
simulador não é aberto nem possui um botão para este assistente.

1. Escolha o CSV da rota e a pasta do trem. O assistente lê `train.ai` ou `train.dat`
   e exige `extensions.cfg`, como o carregador de modelos externos dos serviços IA.
2. Confira as estações, posições em metros, tempo de parada em segundos e portas
   (`L`: esquerda, `R`: direita, `B`: ambos os lados, `0`: fechadas).
   As estações vêm do parser do próprio OpenBVE: não de expressões regulares no CSV.
   Os pontos de parada específicos para o número de carros têm preferência.
3. Defina de 1 a 32 composições, intervalo em segundos, velocidade do plano e
   horário inicial em segundos desde meia-noite. Clique em **Instalar**.

O assistente cria `NomeOriginal_IA.csv` na mesma pasta da rota e uma pasta de
definição XML de IA. A rota original é preservada byte a byte, incluindo sua
codificação, e o trem é referenciado por caminho relativo, sem duplicar seus arquivos.
A cópia fica na mesma pasta para manter as referências relativas do conteúdo.
Antes de confirmar a instalação, o app carrega a cópia completa pelo parser do
OpenBVE e verifica a quantidade de composições, os modelos externos e os drivers
de IA. Se essa verificação falhar, os arquivos gerados são removidos e a operação
não é anunciada como concluída. Após a instalação, selecione o caminho informado
na janela normal do simulador. O app depende dos arquivos do runtime ao lado do
executável; não transporte apenas o EXE isolado.

## Editar uma instalação

Selecione o CSV `_IA.csv` no aplicativo. O trem, as paradas editadas, a quantidade,
o intervalo, a velocidade, o horário e a opção de distribuição inicial são
recuperados da configuração salva. Instalar novamente atualiza o mesmo arquivo
e substitui a referência ao serviço gerado, sem adicionar uma segunda frota.
Se a validação de uma edição falhar, o CSV e o XML anteriores são restaurados.
Arquivos antigos com sufixo ` - IA-xxxxxxxxxx` também podem ser abertos e migrados
para o nome `_IA.csv`; a cópia antiga é preservada.

## Sentidos e trilhos

O serviço gerado segue o sentido do CSV pelo trilho do jogador, índice 0.
Não presume que o trilho 1 exista ou represente o sentido oposto. Rotas com um CSV
por sentido devem receber uma instalação em cada CSV; isso não conecta os serviços
em uma circulação de ida e volta. Ramais, AMVs, conflitos entre vias distintas e
retorno em outro trilho exigem um itinerário específico e não são inferidos.

Os serviços usam proteção de tráfego e preferência por aguardar em estação.
O intervalo é uma solicitação: a ocupação da via pode atrasar partidas.
`DisposeAtDestination` encerra a viagem após o tempo efetivamente percorrido,
incluindo os atrasos de tráfego. A frota é um conjunto limitado de composições
reutilizadas pelo controlador de intervalos após encerrar suas viagens.
A opção **Iniciar com trens distribuídos pela linha** vem ativada. Ela prepara
composições em pontos adiante do jogador, respeitando o intervalo solicitado e
as verificações de ocupação. Assim, o tráfego não depende de o jogador deixar a
primeira plataforma para começar a aparecer. Composições excedentes à duração
do percurso ficam na fila da origem; os horários futuros continuam sendo
respeitados. Com a opção desativada, toda a frota parte da primeira parada e
aguarda a liberação da área de nascimento quando o jogador estiver nela.

O teto de velocidade é definido pelo usuário; o assistente não extrai um perfil
de limites de velocidade. Tráfego preexistente na rota é mantido: adicionar uma
frota ao CSV da Mobitrens que já possui IA soma os serviços novos aos existentes.
Trens somente XML e consistências MSTS não são aceitos por esta versão do assistente.

## Verificação

- `dotnet run --project native/dotnet10/TrafficWizardTests -- "Train/Serie 1000 Cofebra"`
- `dotnet run --project native/dotnet10/RepeatTests -c Release`
- `OpenBve.TrafficInstaller.exe --traffic-test=<pasta>` verifica as três etapas e
  instala e edita uma cópia de teste da Mobitrens, verificando a frota com o
  carregamento completo da rota e dos modelos externos pelo OpenBVE.
- O diagnóstico `/native-traffic-test=2 /native-smoke=45` na rota gerada verifica
  tráfego ativo à frente e movimento sem deslocar o jogador da plataforma inicial.

O teste de geração verifica codificação, preservação do original, caminho relativo
do trem, quantidade, intervalo, posições, ausência de referência ao trilho 1 e
rejeição de paradas inválidas. Os testes de circulação incluem o encerramento de
um serviço sem retorno e regressões dos serviços existentes.
