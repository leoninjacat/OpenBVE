# Validação das texturas — Windows

## Causa e correção

O primeiro inventário encontrou 42 arquivos com extensão BMP danificados em
`Railway/Object/Mobitrens`: assinaturas ausentes, conteúdo de outros formatos,
dados nulos e PNGs truncados. O mesmo erro apareceria no pacote anterior.
Todos tinham cópias decodificáveis nos mesmos caminhos de `Mobitrens_emissive`,
com os mesmos tamanhos. Essas cópias restauraram os 42 arquivos; os originais
e os hashes de comparação foram preservados localmente em `artifacts`.

O carregador PNG legado também emitia erros para quatro PNGs RGB válidos com
transparência tRNS sem paleta. O plugin nativo usa agora o decodificador Skia
nesse caminho, preservando transparência e rejeitando pixels incompletos.

## Evidências

| Conteúdo | Imagens consultadas e decodificadas | Falhas |
| --- | ---: | ---: |
| Data | 414 | 0 |
| Railway | 1.264 | 0 |
| Train | 215 | 0 |
| Total | 1.893 | 0 |

O teste negativo com os 42 arquivos originais danificados falhou como esperado.
Os testes existentes de gráficos, alpha, JPEG e GIF passaram.
Oito arquivos CSV incluídos foram abertos com `--texture-test --smoke=2`:
todos passaram no carregamento das texturas registradas na GPU, sem erros BMP.
A rota de exemplo da Linha 1 carregou 198 texturas, também na cópia extraída
do ZIP final. A auditoria da cópia extraída confirmou as 1.893 imagens válidas.

O empacotador repete as consultas de dimensões e decodificação com o DLL exato
do pacote, verifica sua identidade e bloqueia o ZIP diante de qualquer falha
ou diagnóstico de erro do plugin. `VALIDACAO.txt` registra contagens e hash.

## Limites

São verificadas imagens BMP, PNG, GIF, JPEG e TIFF presentes no pacote e o
carregamento das texturas registradas nos testes das rotas incluídas.
Isso não valida arquivos de terceiros nem certifica todos os aspectos das rotas.
Há avisos de objetos ausentes e referências de estruturas nas rotas originais;
eles são independentes da corrupção dos BMPs e não foram ocultados.
O pacote portátil deve ser extraído em uma pasta separada da instalação existente.
