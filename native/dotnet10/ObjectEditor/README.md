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

- **Ctrl+O** ou pasta: abrir objeto B3D/CSV; também aceita arrastar arquivo.
- **Ctrl+S** ou **Salvar e atualizar**: grava o código e recarrega a geometria,
  preservando a câmera e o histórico de edição.
- Painel redimensionável **Lateral**, **Abaixo** ou **Janela independente**.
- **Globo e arraste**: orbitar acompanhando o gesto; **MMB**: órbita tradicional; **Shift+MMB**: pan; botão direito: olhar.
- **WASD/QE**: mover; roda: zoom; **Home/Num 5**: enquadrar; **F**: aramado.
- **F9**: mensagens do parser; **F5**: reler o arquivo salvo; **F8**: ocultar código.

O salvamento mantém codificação/BOM e CRLF; alterações externas bloqueiam a
sobrescrita. Fechar ou trocar de arquivo com alterações pede salvar, descartar
ou cancelar. Erros de parsing ficam nas mensagens e o texto continua editável.
Texturas usam os caminhos relativos ao arquivo original.

## Edição gráfica de modelos existentes

- Clique numa peça na vista 3D para selecionar seu bloco `[MeshBuilder]` / `CreateMeshBuilder`.
  Um retângulo azul destaca a seleção; clicar no fundo limpa a seleção.
- O painel mostra identificação, vértices, faces e dimensões do mesh selecionado.
- Na barra superior, escolha **Mover**, **Girar** ou **Escalar** e depois o eixo
  **X**, **Y** ou **Z**. Arraste o mesh com o botão esquerdo para manipular somente
  o eixo escolhido. Nenhuma transformação acontece antes de escolher o eixo.
- **Escalar** oferece também **ALL**: a mesma proporção é aplicada aos três eixos
  do mesh selecionado. As outras peças do arquivo permanecem intactas.
- Na rotação e escala, arrastar para a direita/cima aumenta o valor; para a
  esquerda/baixo diminui. Na movimentação, o gesto acompanha a projeção do eixo
  na tela; eixos de frente para a câmera usam o gesto para cima/direita.
- **Esc** volta à seleção. Os campos X/Y/Z do painel permitem ajustes numéricos
  de posição, **rotação em graus nos três eixos** e escala positiva. O painel,
  a geometria e o código são atualizados tanto pelo arraste quanto pelos campos.
- **Dimensões (m)** mostra as medidas reais X/Y/Z da caixa delimitadora nos eixos
  do modelo e permite digitar medidas diretamente. O editor calcula o redimensionamento
  após a rotação, considerando também comandos `*All` posteriores. Eixos planos
  permanecem com dimensão zero; medidas incompatíveis exibem erro sem alterar o código.
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
Ainda não inclui criação de primitivas, edição de faces ou seleção de texturas. A edição gráfica deve ser feita com o código sintaticamente
válido; diagnósticos do parser continuam disponíveis em F9.

Validação integrada (modifica a cópia, dobrando a geometria):

```sh
cp native/dotnet10/ObjectEditor/Samples/cube.b3d /tmp/object-test.b3d
native/run-objecteditor.sh /tmp/object-test.b3d --editor-test --smoke=14
```

Exigir saída zero, `OBJECT_EDITOR_GEOMETRY_OK`, `VIEWER_EDITOR_OK`, `OBJECT_EDITOR_GRAPH_OK`,
`OBJECT_EDITOR_PROPERTIES_OK`, `OBJECT_EDITOR_TOOLS_OK`, `OBJECT_EDITOR_PIVOT_OK` e
`VIEWER_SMOKE_OK`. O teste percorre os três layouts, salva pelo widget GTK e
confere a nova geometria e a câmera preservada. Também seleciona o primeiro mesh,
altera a escala pelo widget de propriedades, verifica a prévia sem salvar, desfaz,
move e salva novamente. Confere a cor carregada, altera RGBA pelo seletor, desfaz,
redimensiona meshes normais, rotacionados e planos, e testa a direção do globo. Em arquivos com vários meshes, verifica que a alteração
não afetou as outras peças. Os botões de mover/girar/escalar são testados em cada
eixo, incluindo ALL, sincronização dos campos de rotação e desfazer. A validação do
pivô confirma que XYZ/origem/centro não alteram o modelo, que a configuração persiste,
que rotação/escala/dimensões mantêm o ponto fixo e que conflitos externos são preservados. Repetir com `cube.csv` testa CSV; uma cópia de
`meshes.b3d` testa a seleção de cilindros e a independência entre blocos.
