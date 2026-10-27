# Simulador Windows — IA e CCO beta

Integra os fontes Linux do commit `72d5ff4ef612c19d46cec25d5e614e52643bfd58`.
O motor e os testes de tráfego são compartilhados entre Linux e Windows.
As alterações locais do Route Viewer e do assistente de rotas foram preservadas.

Na raiz, abra `Iniciar-OpenBVE.cmd` para a janela original de seleção de rota,
trem, configurações, controles e pacotes. `Iniciar-CCO.cmd` abre a mesma janela
com o modo CCO pré-selecionado. Escolha a rota noturna Gatos–Cachorros para os
serviços IA incluídos. O executável fica em `native/dotnet10/openbve-windows-beta`.
Mantenha as DLLs e a pasta Data junto ao executável. No Windows, configurações
e logs ficam em UserData/Settings ao lado dele.

`OpenBVE-Route-Editor.cmd` abre o Route Viewer com editor integrado. O instalador
inclui esse aplicativo separadamente em `Editor`, com atalho próprio e runtime.

O Windows reutiliza os arquivos `formMain` e os demais formulários de
`source/OpenBVE/UserInterface`, com seus recursos e Designer originais.
`prepare-classic-ui.ps1` gera apenas os aliases e adaptadores de integração
necessários ao motor Skia/.NET 10; não recria nem redesenha a janela.

Para recompilar:

```powershell
./native/dotnet10/build-openbve.ps1
```

O build inclui o runtime .NET 10 e as bibliotecas Windows. A primeira compilação
pode exigir acesso ao NuGet. O aplicativo compilado não exige o SDK.

## Controles

- F7: mapa com curvas, diagrama e oculto.
- Numpad 1/7: câmera da estação anterior/seguinte no CCO.
- Numpad 5: restaura a câmera da estação.
- Ctrl+F7: alterna o rádio CCO.
- No CCO o piloto automático permanece ativo.

## Tráfego

A rota noturna usa oito composições da Linha 1 em circulação, com intervalo
mínimo de 390 segundos, e serviços da Linha 2 com intervalo de 240 segundos.
Inclui espera na estação anterior, separação de trens e reserva de manobras.
Esta versão preserva as rotas fixas de cada serviço: escolha dinâmica entre
as duas plataformas de Cachorros ainda não é implementada por esses XMLs.

As 83 verificações RepeatTests passaram no Windows. Os 14 áudios de estação e rádio
transferidos foram conferidos pelo hash Git. Os dois áudios de destino
`destino_cachorros.wav` e `destino_gatos.wav` não puderam ser transferidos pelo
conector; não estão incluídos. Os anúncios correspondentes ficam ausentes.

As alterações estão nos arquivos de trabalho; a referência Git local não foi
avançada, pois esta sessão não pode escrever em .git.

A detecção automática Tolk fica desativada neste runtime beta: a biblioteca
nativa encerrava o processo antes da janela 3D nesta máquina. Para testar
explicitamente o leitor de tela, defina `OPENBVE_SCREEN_READER=1` antes de abrir.

Teste real Windows concluído: rota noturna carregada com 15.435 objetos estáticos,
20 instâncias de serviços IA; piloto automático ativo, câmeras independentes,
restauração por estação, ciclo F7, diagrama terminal e cores de trens aprovados.
Registro: `native/dotnet10/artifacts/cco-beta-smoke.log`.
Os modelos existentes ainda emitem avisos de sintaxe e sons ausentes no log.
