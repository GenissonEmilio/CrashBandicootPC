# CrashBandicootPC — origem e validação

Este fork mantém o histórico de `Matteo842/CrashBandicoot-Launcher` até o commit
`d529196adc463158a45bea4807f15b07a8a28b39` (base 2.3.1), incluindo o runtime e o
recompilador RecompOne, os créditos de wurlyfox/c1 e ManDude/goocdump, e as licenças.
O launcher de `tools/desktop` é adaptado de `GenissonEmilio/ForbiddenMemoriesPC`
no commit `f6e84d47945539540569d3dcb9ff24d0dc0db414`, sob MIT. As novas ilustrações
vetoriais são desenhadas em Cairo; o ícone do Crash vem da base original.

O motor específico do Yu-Gi-Oh não executa Crash. O que se reaproveita é a
experiência de instalação: escolher um disco, preparar em segundo plano, manter
o jogo lembrado, abrir pelo menu do Linux e preservar dados fora da instalação.
Crash usa seu próprio recompilador e runtime. Seu código MIPS é traduzido em C#
e compilado localmente; a distribuição inclui apenas ferramentas e runtime.

## Alterações deste fork

* Interface GTK em português, com identidade do Crash, seleção e arraste de
  arquivo, controles, saves e instalação de atalho.
* Importação com detecção de formato real. O arquivo do usuário tem extensão ZIP
  e formato 7z. O leitor aceita ZIP/RAR/7Z e arquivos aninhados com limites de
  tamanho/quantidade, rejeitando links e caminhos que saiam da pasta temporária.
* A imagem Mode 2 precisa conter `SCUS_949.00` com SHA-256 completo
  `aabf1464f90b2e0b81e712b77aebbdb88f303b16ce830535e2b0cd886ee280f2`, o boot
  correspondente e arquivos NSD/NSF presentes e acessíveis até o último setor em S0–S3. O CUE gerado aponta apenas
  para a cópia gerenciada. O original é preservado.
* Dados em `$XDG_DATA_HOME/CrashBandicootPC` ou `~/.local/share/CrashBandicootPC`.
  `CRASH_PC_DATA_DIR` permite que o motor instalado seja somente leitura, com
  configuração, cache e saves na pasta do usuário. Cópias gerenciadas corrompidas
  são refeitas a partir da origem selecionada.
* A chave do cache inclui os módulos do launcher, runtime e recompilador, além
  do JSON de recompilação e do patch. Atualizações invalidam código antigo;
  reiniciar ou mover a instalação não força uma nova recompilação.
* Pacote Linux x86-64 com GTK/Python e .NET self-contained. As variáveis de
  biblioteca do processo GTK não vazam para o motor. O motor usa globalização
  invariante para dispensar ICU instalado. A preparação local limita o pool
  .NET a dois processadores; o fallback Mesa usa dois threads.
* O workflow da base para releases de outras plataformas foi substituído pela
  compilação e validação Linux. Nenhum disco ou código gerado é usado na CI.

## Testes reproduzíveis

```sh
python3 -m unittest discover -s tests/desktop -v
sh tools/desktop/release/build-linux.sh
python3 tools/desktop/validate_local.py /caminho/Crash.7z \
  --program dist/CrashBandicootPC --data-dir artifacts/local-check
```

Os testes sem disco usam ISO9660 sintético original para conferir hash completo,
arquivos modificados, normalização do CUE, formatos com nome errado, travessia de
caminhos, links, limites, múltiplos discos, preservação da seleção e reparo de cache.

A validação local com disco real mede reutilização do DLL sem reescrevê-lo,
altera uma cópia do JSON para provar invalidação e restaura a chave original.
Em N. Sanity Beach, injeta entradas normais de movimentação, pulo e giro,
captura o framebuffer e verifica que um estado salvo restaura a posição após
movimento. Outras amostras verificam abertura/render de menu/mapa, Slippery Climb
e Dr. Neo Cortex. Cada processo usa seus próprios saves de teste e termina por
uma exceção de encerramento normal da sessão, sem comandos de energia do sistema.

O pacote também é verificado em Ubuntu 22.04 limpo com Xvfb/Mesa, sem Python,
.NET SDK/runtime ou GTK instalados: abre o launcher e executa preparação e
gameplay com o disco montado localmente. Esse disco não entra na imagem Docker,
no repositório, nos artefatos de CI ou no release.

Os relatórios e capturas ficam em `artifacts/` e não são versionados. Esses
testes não são uma campanha completa jogada até 100%. O runtime mantém as
limitações conhecidas da base; melhorias como widescreen e FPS alto são opcionais.
