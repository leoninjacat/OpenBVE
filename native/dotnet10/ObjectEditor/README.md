# OpenBVE Object Editor — edição gráfica inicial

Editor Linux nativo (.NET 10 + GTK/X11 ou XWayland), com viewport OpenGL e
código B3D/CSV editável. Compartilha o painel, o salvamento e o renderizador do
Route Editor. O carregamento usa diretamente o parser Object.CsvB3d; CSV aqui
é um arquivo de objeto, não de rota.

Na raiz do repositório:

```sh
native/dotnet10/build-objecteditor.sh
native/run-objecteditor.sh
# Ou abrir diretamente um exemplo:
native/run-objecteditor.sh native/dotnet10/ObjectEditor/Samples/cube.b3d
```

O script de abertura compila automaticamente se o runtime ainda não existir.
Após mudanças nos fontes, execute o build novamente, com o editor fechado.
Requer SDK .NET 10, GTK4/libadwaita e as mesmas dependências do Route Editor.
O script acima usa o runtime de desenvolvimento próprio. Para instalar no menu
junto ao simulador e ao Route Editor, execute `bash native/install-linux.sh --build`.
Depois abra **OpenBVE Object Editor**, ou use:

```sh
flatpak run --command=object-editor-native org.openbve.OpenBVE.Native
```

- **Novo**, na barra superior do viewport, antes de Abrir: escolha **Arquivo B3D**,
  **Arquivo CSV** ou **Arquivo vazio…** e o destino. Arquivo vazio pede a linguagem
  B3D/CSV e cria um arquivo sem conteúdo; a extensão informa ao editor qual sintaxe usar.
  O editor cria um arquivo vazio nesse formato, sem sobrescrever arquivos existentes.
  Também é possível iniciar sem arquivo e usar esse menu.
- Na lateral esquerda do viewport, os ícones de **Cubo**, **Plano** e **Cilindro**
  inserem meshes com a sintaxe do arquivo
  aberto. Configure as medidas em metros no painel direito antes de clicar: cubo usa largura/altura/
  profundidade; plano usa largura/profundidade no plano XZ; cilindro usa diâmetro,
  altura e segmentos (3–256), no eixo Y, com tampas. As formas ficam na origem.
  A inserção atualiza a prévia e pode ser desfeita com **Ctrl+Z** no código;
  **Ctrl+S** salva as alterações.
- **Ctrl+O** ou pasta: abrir objeto B3D/CSV; também aceita arrastar arquivo.
- **Ctrl+S** ou o ícone de disquete **Salvar e atualizar** no viewport (no lugar
  de recarregar): grava o código e recarrega a geometria,
  preservando a câmera e o histórico de edição.
- Painel redimensionável **Lateral**, **Abaixo** ou **Janela independente**,
  com o seletor ao lado de **Código**, acima do texto.
- **Globo e arraste**: orbitar acompanhando o gesto; **MMB**: órbita tradicional; **Shift+MMB**: pan; botão direito: olhar.
- **WASD/QE**: mover; roda: zoom; **Home/Num 5**: enquadrar; **F**: aramado.
- **F9**: mensagens do parser; **F5**: reler o arquivo salvo; **F8**: ocultar código.

O salvamento mantém codificação/BOM e CRLF; objetos vazios ou somente ASCII usam
UTF-8 para permitir comentários acentuados. Alterações externas bloqueiam a
sobrescrita. Fechar ou trocar de arquivo com alterações pede salvar, descartar
ou cancelar. Erros de parsing ficam nas mensagens e o texto continua editável.
Texturas usam os caminhos relativos ao arquivo original.

## Edição gráfica de modelos existentes

- Clique numa peça na vista 3D para selecionar seu bloco `[MeshBuilder]` / `CreateMeshBuilder`.
  Um retângulo azul destaca a seleção; clicar no fundo limpa a seleção.
- Ao selecionar uma mesh no viewport, o código correspondente é destacado e
  trazido para a área visível do painel, sem transferir o foco do viewport.
  Se o painel estava oculto, ele volta a aparecer. O restante do arquivo continua editável.
- **Delete com foco no viewport** exclui a mesh selecionada e seu bloco do código.
  Comandos globais (`RotateAll`, `ScaleAll`, etc.) são preservados para não mudar
  as outras peças. A tecla Delete no editor de texto mantém a edição normal do texto.
  A exclusão atualiza a prévia, limpa a seleção e pode ser desfeita com **Ctrl+Z no código**.
- **Nome**, nas propriedades: escreva um nome para a mesh. O editor o salva como
  comentário `; @openbve-name Nome` dentro do bloco, compatível com B3D/CSV.
  Renomear não muda a geometria nem a identificação usada pelos pivôs; apagar o
  nome remove o comentário. Renomeações também podem ser desfeitas no código.
- O painel mostra identificação, nome, vértices, faces e dimensões do mesh selecionado.
- Na barra superior, escolha **Mover**, **Girar** ou **Escalar** e depois o eixo
  **X**, **Y** ou **Z**. Arraste o mesh com o botão esquerdo para manipular somente
  o eixo escolhido. Nenhuma transformação acontece antes de escolher o eixo.
- **Escalar** oferece também **ALL**: a mesma proporção é aplicada aos três eixos
  do mesh selecionado. As outras peças do arquivo permanecem intactas.
- **Girar → ALL** usa o último eixo X/Y/Z escolhido (Y inicialmente) e gira o
  objeto inteiro em torno da origem, acrescentando `RotateAll x,y,z,graus` em B3D
  ou `RotateAll,x,y,z,graus` em CSV ao fim do código. O eixo é capturado no início
  do arraste; trocar de eixo depois não redireciona uma operação já enviada.
  **Ctrl+Z no código** desfaz a rotação global; gravar continua sendo explícito.
- O ícone de **pessoa** à esquerda mostra/oculta uma silhueta de **1,75 m**, em pé
  ao lado do modelo e alinhada à sua base, como referência de tamanho. É apenas
  uma sobreposição da prévia: não é selecionável, não altera medidas ou meshes,
  não entra no histórico e não grava conteúdo nem arquivos auxiliares.
- Na rotação e escala, arrastar para a direita/cima aumenta o valor; para a
  esquerda/baixo diminui. Na movimentação, o gesto acompanha a projeção do eixo
  na tela; eixos de frente para a câmera usam o gesto para cima/direita.
- **Esc** volta à seleção. Os campos X/Y/Z do painel permitem ajustes numéricos
  de posição, **rotação em graus nos três eixos** e escala positiva. O painel,
  a geometria e o código são atualizados tanto pelo arraste quanto pelos campos.
- **Dimensões (m)** mostra as medidas reais X/Y/Z da caixa delimitadora nos eixos
  do modelo e permite digitar medidas diretamente. O editor calcula o redimensionamento
  considerando a rotação e os comandos `*All` posteriores. Eixos planos
  permanecem com dimensão zero; medidas incompatíveis exibem erro sem alterar o código.
- As escalas do painel e os ajustes de dimensões são aplicados antes da rotação,
  nos eixos locais da peça. Girar em X/Y/Z preserva as distâncias entre vértices,
  inclusive em cubos achatados e cilindros redimensionados. Dimensões incompatíveis
  com a orientação atual são recusadas em vez de deformar a peça.
- **Cor** carrega a cor RGBA do material e abre um seletor com opacidade. A escolha
  atualiza código e prévia; em meshes com várias cores, a amostra mostra a primeira
  e a alteração aplica a nova cor a todo o mesh selecionado.
- Posição, rotação e escala são transformações **relativas à geometria original do bloco**.
  Rotação e escala usam o pivô configurado; sem configuração, usam a ancoragem padrão da mesh.
  Comandos `*All` em blocos posteriores continuam sendo aplicados.
- As alterações gráficas atualizam o código e a prévia automaticamente, sem gravar
  o arquivo. A edição direta do texto também atualiza a prévia após uma breve pausa.
- **Ctrl+Z/Ctrl+Shift+Z no código** desfaz/refaz as alterações gráficas. **Ctrl+S**
  grava usando as proteções de codificação e alteração externa já existentes.
- O editor mantém as instruções originais e acrescenta um trecho identificado por
  `; @openbve-editor` ou `; @openbve-color` ao final do bloco. Esses trechos contêm comandos B3D/CSV
  normais, compatíveis com o simulador; mudanças seguintes substituem o trecho.

## Pivô / ancoragem da mesh

- O marcador amarelo mostra o pivô atual. Quando ele difere da ancoragem padrão,
  outro marcador, cinza, identifica essa ancoragem. Indicadores na borda avisam
  quando o ponto fica fora da vista. A ancoragem padrão é a origem local do
  modelo, acompanhando as transformações da peça; não é calculada pelo seu centro.
- Escolha a ferramenta **Pivô**, selecione **X**, **Y** ou **Z** e arraste o
  marcador ou a mesh selecionada. A geometria permanece no mesmo lugar.
- Os campos **Pivô (m)** permitem informar X/Y/Z diretamente, nos eixos do modelo.
  **Origem** posiciona o pivô em `(0, 0, 0)` do modelo. **Centro do objeto** usa o
  centro da caixa delimitadora da mesh selecionada.
- Definir, mover ou centralizar o pivô não modifica o texto, a geometria ou o
  arquivo B3D/CSV, nem marca o código como alterado. A configuração é salva
  automaticamente em `nome.b3d.objecteditor.json` ou `nome.csv.objecteditor.json`,
  ao lado do objeto. Não é preciso salvar o modelo para guardar o pivô.
- Girar, escalar ou alterar dimensões depois disso usa o pivô escolhido e atualiza
  comandos B3D/CSV normais, inclusive a translação necessária para manter esse
  ponto fixo. Só essas transformações modificam o código; **Ctrl+S** grava o modelo.
  Ao mover a mesh, seu pivô acompanha a peça, inclusive ao desfazer no código.
- Copie o arquivo auxiliar junto com o objeto para preservar os pivôs em outro
  local. Os pivôs são identificados pelo conteúdo original de cada bloco, ignorando
  os trechos gerados pelo editor. Alterar o bloco original pode exigir redefinir
  sua ancoragem. Mudanças externas no arquivo auxiliar bloqueiam a sobrescrita;
  reabra o objeto para carregar a configuração mais recente.

Exemplo com duas colunas e uma placa:

```sh
native/run-objecteditor.sh native/dotnet10/ObjectEditor/Samples/meshes.b3d
```

Esta etapa trabalha com blocos MeshBuilder explícitos em linhas separadas.
Ainda não inclui edição de faces ou seleção de texturas. A edição gráfica deve ser feita com o código sintaticamente
válido; diagnósticos do parser continuam disponíveis em F9.

Validação integrada (modifica a cópia, dobrando a geometria):

```sh
cp native/dotnet10/ObjectEditor/Samples/cube.b3d /tmp/object-test.b3d
native/run-objecteditor.sh /tmp/object-test.b3d --editor-test --smoke=14
```

Exigir saída zero, `OBJECT_EDITOR_GEOMETRY_OK`, `VIEWER_EDITOR_OK`, `OBJECT_EDITOR_GRAPH_OK`,
`OBJECT_EDITOR_PROPERTIES_OK`, `OBJECT_EDITOR_TOOLS_OK`, `OBJECT_EDITOR_PIVOT_OK`,
`OBJECT_EDITOR_MESH_ACTIONS_OK` e
`VIEWER_SMOKE_OK`. O teste percorre os três layouts, salva pelo widget GTK e
confere a nova geometria e a câmera preservada. Também seleciona o primeiro mesh,
altera a escala pelo widget de propriedades, verifica a prévia sem salvar, desfaz,
move e salva novamente. Confere rotações rígidas X=-45°, Y=45° e Z=40° em cubos/cilindros com escala
não uniforme, comparando todas as distâncias entre vértices antes/depois.
Confere a cor carregada, altera RGBA pelo seletor, desfaz,
redimensiona meshes normais, rotacionados e planos, e testa a direção do globo. Em arquivos com vários meshes, verifica que a alteração
não afetou as outras peças. Os botões de mover/girar/escalar são testados em cada
eixo, incluindo ALL, sincronização dos campos de rotação e desfazer. A validação do
pivô confirma que XYZ/origem/centro não alteram o modelo, que a configuração persiste,
que rotação/escala/dimensões mantêm o ponto fixo e que conflitos externos são preservados. Repetir com `cube.csv` testa CSV; uma cópia de
`meshes.b3d` testa a seleção de cilindros e a independência entre blocos.

Teste da geração de primitivas e desfazer pelo buffer GTK:

```sh
make -C native/linux-launcher check-object-creation
```
