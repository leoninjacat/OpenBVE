# Route Viewer para Windows — OpenBVE by LT ARTS

## Nova rota

O botão **Nova rota** no editor, o botão **+** na barra do viewport e **Ctrl+N**
abrem um assistente em três etapas: nome e destino, biblioteca de objetos,
confirmação. Comprimento e bitola são configuráveis. O projeto usa
`Railway/Route`, `Railway/Object` e `Railway/Sound`, com trilho zero e terreno
gerados em CSV quando nenhum modelo inicial é escolhido. O CSV abre automaticamente
no editor e na cena 3D. Arquivos existentes não são sobrescritos; uma rota em
edição passa pela confirmação de alterações antes de abrir o assistente.

As abas Via, Plataformas e Cenário permitem escolher pastas para 19 categorias,
cadastrar seus modelos e escolher objetos iniciais. O gerador cria os índices
Structure e os comandos Track apropriados, além de `catalogo-objetos.json`.
Veja [o mapeamento e os limites](ROUTE-OBJECT-WIZARD.md).
Bibliotecas externas precisam estar na mesma unidade; as referências ficam
relativas ao diretório Object do projeto. Para mover ou compartilhar esse caso,
é preciso preservar a relação entre as pastas e as dependências dos modelos.
Sem bibliotecas, a base gerada permanece independente. O gerador `RouteProject.cs` é
independente da interface e pode ser reutilizado no Linux; este wizard usa
Windows Forms.

Validação: navegação do assistente, criação, nomes com acentos e espaços,
biblioteca externa, recusa a sobrescrever arquivos, comprimento inválido e
carregamento/renderização reais de trechos de 500 m e 1.000 m.

O Windows usa o mesmo projeto `native/dotnet10/RouteViewer` do Linux: câmera,
renderização, carregamento, mensagens e gravação segura de rotas são compartilhados.
Este projeto fornece o seletor de arquivos e o editor Windows Forms pelo mesmo
protocolo UTF-8/base64 usado pelo editor GTK. O motor continua responsável por
preservar a codificação/BOM e detectar alterações externas antes de salvar.

## Compilar e abrir

Instale o **SDK .NET 10 x64**, abra um terminal na raiz do repositório e execute:

```powershell
powershell -ExecutionPolicy Bypass -File native/dotnet10/build-routeviewer.ps1 -Run
```

Para abrir uma rota diretamente, acrescente `-Route 'C:\caminho\rota.csv'`.
A primeira compilação precisa de internet para restaurar os pacotes NuGet.
A saída fica em `native/dotnet10/routeviewer-windows/RouteViewer.exe` e inclui
o runtime .NET. Mantenha os outros arquivos e a pasta `Data` junto ao executável.

O editor abre ao carregar uma rota. Tem os modos lateral, abaixo e janela
independente; salvar solicita a gravação ao motor e atualiza a prévia. Abrir
outra rota e fechar passam pela confirmação de alterações não salvas.
F7/Ctrl+O abre rota, F5 recarrega, F8 alterna o editor, F9 mostra mensagens.
Os controles de câmera permanecem os mesmos do Linux.

## Estado de validação

Compilação e teste de integração no Windows concluídos com a rota Mobitrens
(11.132 objetos e 13 estações): lateral, abaixo, independente, reencaixe,
salvamento de uma cópia, recarga da prévia e preservação da câmera.
O envio do documento ao editor ocorre em segundo plano para evitar bloquear
as mensagens de janela usadas pelo encaixe Win32.
Ainda verificar manualmente arrastar a câmera, cancelar fechamento e conflitos
com alterações feitas externamente. Falhas do editor são registradas em
`desktop-error.log`; falhas gerenciadas do motor em `routeviewer-error.log`.
Os testes `--ui-test` e `--feedback-test` existentes injetam eventos X11 e são
exclusivos do Linux. Não os execute no Windows.

## Teclado e pacote portátil

O viewport Win32 incorporado recebe foco real mesmo quando `GameWindow.Focused`
do OpenTK permanece falso. O Windows consulta o foco nativo do HWND filho e
recebe suas mensagens de teclado diretamente; o Linux conserva o backend OpenTK.
`--keyboard-test --smoke=8`, com uma rota de teste, verifica W nos três layouts,
isolamento do editor e os atalhos F1/Tab por mensagens reais da janela.

Para gerar o ZIP completo com conteúdo de teste:

```powershell
powershell -ExecutionPolicy Bypass -File native/dotnet10/package-routeviewer.ps1 -IncludeDemo
```

Sem `-IncludeDemo`, gera apenas o aplicativo. `-SkipBuild` empacota a build já
existente. A saída fica em `native/dotnet10/artifacts/`, com SHA-256 ao lado.
O pacote inclui .NET e bibliotecas Windows, instruções e licenças; remove símbolos
de debug e logs locais. Não precisa do SDK instalado na máquina que vai testar.
O marcador `portable.flag` mantém configurações e logs em `UserData` no pacote.
Antes de criar o ZIP, o empacotador consulta as dimensões e decodifica todas
as imagens suportadas de `Data` e, se incluídas, `Railway` e `Train`, usando
o próprio `Texture.Native.dll` do pacote. Qualquer falha ou diagnóstico de erro
interrompe a criação. `VALIDACAO.txt` registra contagens e o SHA-256 do plugin.
Essa garantia cobre o conteúdo incluído, não rotas externas de outras origens.
O teste `RouteViewer.exe ROTA.csv --texture-test --smoke=4` também força o
carregamento de todas as texturas registradas da rota na GPU e rejeita erros
de textura no log, inclusive referências BMP ausentes. Executado na rota
Mobitrens Linha 1 - Gatos-Cachorros: 198 texturas carregadas, nenhuma falha.
