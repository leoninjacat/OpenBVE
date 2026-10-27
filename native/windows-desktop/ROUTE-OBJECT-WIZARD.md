# Bibliotecas por categoria no assistente de rotas

Referência: [CSV route format — documentação oficial](https://openbve-project.net/documentation/HTML/route_csv.html), namespaces Structure e Track.

O assistente separa 19 categorias em três abas. Selecionar uma pasta cadastra
seus objetos e subpastas, com índices estáveis por categoria. O usuário escolhe
explicitamente um objeto inicial ou conserva apenas o cadastro.

| Grupo | Categorias | Uso inicial |
| --- | --- | --- |
| Via | Rail, Ground | Modelo escolhido no índice 0; senão, base gerada |
| Via | WallL/R, DikeL/R | Um lado ou ambos; início no metro 0 |
| Via | Pole | Uma via, definição Pole(0;1), intervalo de 50 m |
| Plataformas | FormL/R, RoofL/R | Primeiros 100 m, em blocos de 25 m; cobertura exige plataforma do mesmo lado |
| Plataformas | FormCL/CR, RoofCL/CR | Cadastro para trechos entre vias |
| Cenário | FreeObj | Um objeto inicial a 5 m à direita; outros ficam cadastrados |
| Cenário | Beacon, CrackL/R | Cadastro; posicionamento e parâmetros configurados no CSV |

As peças centrais e fendas aceitam CSV/B3D estáticos. As demais também aceitam
ANIMATED. X consta da documentação geral, mas não é oferecido pelo assistente
desta build nativa, que não distribui um plugin X. Nomes de arquivos não provam
o papel geométrico dos modelos; escolha a pasta e o modelo correspondentes.

O CSV inclui as declarações Structure e os comandos Track necessários para
os modelos escolhidos. Roof reserva índice zero para ausência de cobertura;
as famílias opcionais começam em 1. Esquerda/direita compartilham o índice
do conjunto. O `catalogo-objetos.json` registra os índices e referências.

Os modelos e suas texturas não são copiados ou alterados. Seus caminhos são
relativos a Railway/Object e devem permanecer válidos ao mover o projeto.
Bibliotecas em outra unidade ou com delimitadores incompatíveis são recusadas
antes da criação. Estações, horários, sinalização e sistemas de segurança não
são inferidos a partir da geometria.

Testes: 19 categorias, ordenação/seleção de índices, retorno entre etapas,
restrições de formato, pares laterais, Pole, Form/Roof, fallback e recusa de
cobertura sem plataforma. A rota resultante foi carregada no motor nativo,
com 333 objetos, sem erros de estrutura ou textura; os avisos de compatibilidade
do conjunto padrão de sinais são independentes do assistente.
