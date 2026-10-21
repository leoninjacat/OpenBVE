# Correção de texturas Linux — 21/10/2026

A auditoria reproduziu as 42 falhas BMP da versão Windows. Os arquivos em
`Railway/Object/Mobitrens` continham dados inválidos; as cópias correspondentes
em `Mobitrens_emissive` passaram na consulta de dimensões e decodificação pelo
plugin Linux e tinham os mesmos tamanhos. Foram usadas para restaurar os
arquivos, sem alterar objetos, coordenadas de textura ou cores.

O PNG legado também reportava falsamente como inválidos quatro PNGs RGB com
tRNS sem PLTE. Assim como no Windows, o plugin nativo agora usa Skia para PNG,
preservando a transparência. Erros no fallback também retornam falha pelo
contrato do plugin, sem deixar escapar uma exceção de decodificação.

`TextureAudit` consulta dimensões, decodifica as imagens e verifica os
diagnósticos usando o caminho exato da DLL indicada. O empacotador executa a
auditoria em Data, Railway e Train com a DLL do staging e aborta se uma imagem
falhar. A identidade e o SHA-256 do plugin ficam nos relatórios.

Os testes nativos verificam BMP 24 bits com padding e orientação bottom-up,
PNG RGB com chave de transparência sem paleta e rejeição de XML disfarçado de
BMP. Os testes existentes continuam verificando alpha, JPEG e GIF.

O Route Viewer aceita `--texture-test --smoke=2` para carregar na GPU todas as
texturas registradas e falhar diante de erros de texturas, inclusive de BMP.
Avisos independentes sobre objetos ausentes continuam visíveis.

## Resultado da validação

| Conteúdo | Imagens | Falhas |
| --- | ---: | ---: |
| Data | 414 | 0 |
| Railway | 1.264 | 0 |
| Train | 215 | 0 |

O Flatpak instalado passou no teste de upload das texturas nas oito rotas CSV
do diretório `Mobitrens`. A rota de exemplo da Linha 1 carregou 198 texturas,
11.132 objetos e 13 estações. A captura da prévia foi inspecionada.
O teste negativo rejeitou os 42 BMPs originais danificados, como esperado.
