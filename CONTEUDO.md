# Rotas e trens para teste

As pastas `Railway/` e `Train/` contêm a cópia integral do conteúdo local usado
nos testes do port Linux: rotas Mobitrens, seus objetos e sons e o trem
Série 1000 Cofebra. A estrutura original, nomes, arquivos auxiliares e backups
foram preservados. O conteúdo ocupa aproximadamente 920 MB.

Os arquivos são armazenados no Git comum, sem precisar de Git LFS. O GitHub
não permite enviar novos objetos LFS para este fork público. Apenas
`Railway/Sound/L2 verde/SomPlat.wav` (292.221.624 bytes) excede o limite de
100 MB por arquivo: seus bytes ficam em nove partes de até 32 MiB em
`content-parts/SomPlat.wav/`.

`native/prepare-content.sh` recompõe esse áudio no caminho original e verifica
seu SHA-256. O instalador e os comandos de teste executam essa preparação
automaticamente, sem downloads de conteúdo adicionais. Um arquivo existente com
conteúdo diferente é preservado, e o script avisa antes de continuar. Para
preparar o conteúdo separadamente:

```bash
bash native/prepare-content.sh
```

O SHA-256 do áudio original é:
`43410e4277dcb119d9dcd08497dfbb0977dda19874054c8c1d535d76c57d9a3f`.

O conteúdo mantém sua autoria e os avisos que acompanham os arquivos; sua
inclusão não o relicencia sob a licença do motor OpenBVE. Não foram encontrados
arquivos separados de licença nas pastas originais.

As 42 texturas `.bmp` danificadas da pasta `Object/Mobitrens` foram restauradas
a partir das cópias válidas nos mesmos caminhos de `Object/Mobitrens_emissive`.
A validação verifica assinatura, dimensões e decodificação completa, sem
substituições genéricas. O empacotador Windows bloqueia a criação de ZIP se
qualquer imagem incluída falhar no plugin que será distribuído.
O port nativo também não tem suporte
a todos os plugins da versão Mono. Veja `native/packaging/README.md`.

Após instalar o aplicativo, execute da raiz:

```bash
bash native/run-demo.sh viewer
bash native/run-demo.sh train
```

O primeiro comando abre a rota no Route Viewer, com editor em F8 e mensagens em
F9. O segundo inicia uma viagem com o trem incluído. É possível informar outra
rota e trem nos argumentos; consulte `bash native/run-demo.sh --help`.
