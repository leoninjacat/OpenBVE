# OpenBVE Object Editor — etapa 1

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
- **MMB/globo e arraste**: orbitar; **Shift+MMB**: pan; botão direito: olhar.
- **WASD/QE**: mover; roda: zoom; **Home/Num 5**: enquadrar; **F**: aramado.
- **F9**: mensagens do parser; **F5**: reler o arquivo salvo; **F8**: ocultar código.

O salvamento mantém codificação/BOM e CRLF; alterações externas bloqueiam a
sobrescrita. Fechar ou trocar de arquivo com alterações pede salvar, descartar
ou cancelar. Erros de parsing ficam nas mensagens e o texto continua editável.
Texturas usam os caminhos relativos ao arquivo original. Não há ainda botões
para criar primitivas, seleção de faces nem painel de propriedades/texturas.

Validação integrada (modifica a cópia, dobrando a geometria):

```sh
cp native/dotnet10/ObjectEditor/Samples/cube.b3d /tmp/object-test.b3d
native/run-objecteditor.sh /tmp/object-test.b3d --editor-test --smoke=8
```

Exigir saída zero, `OBJECT_EDITOR_GEOMETRY_OK`, `VIEWER_EDITOR_OK` e
`VIEWER_SMOKE_OK`. O teste percorre os três layouts, salva pelo widget GTK e
confere a nova geometria e a câmera preservada. Repetir com `cube.csv` testa CSV.
