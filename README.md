# OTIMIZADOR DE PC

# ==============================================

# 

# © 2026 Pedro Queiroz — Todos os direitos reservados.

# 

# 

# SOBRE O PROJETO

# ==============================================

# 

# O Otimizador de PC é uma ferramenta de manutenção e otimização do Windows desenvolvida em PowerShell.

# 

# O programa foi criado para facilitar tarefas de limpeza, diagnóstico, manutenção, otimização e gerenciamento do sistema em um único aplicativo de terminal.

# 

# O projeto possui diferentes níveis de otimização, ferramentas de diagnóstico, manutenção do Windows, benchmark, gerenciamento de inicialização e sistema de reversão das alterações realizadas.

# 

# 

# RECURSOS

# ==============================================

# 

# O programa possui:

# 

# \- Análise completa do computador

# \- PC Health Score

# \- Otimização Inteligente

# \- Otimização Padrão

# \- Otimização Gamer

# \- Debloat do Windows

# \- Reversão da última otimização

# \- Gerenciamento de programas de inicialização

# \- Manutenção do Windows

# \- Verificação de disco

# \- SFC

# \- DISM

# \- CHKDSK

# \- Limpeza de componentes antigos do Windows

# \- Benchmark de CPU, RAM, disco e boot

# \- Sistema de snapshots e backups

# \- Ponto de restauração do Windows

# \- Interface de terminal personalizada

# \- Sistema de progresso e status das operações

# 

# 

# REQUISITOS

# ==============================================

# 

# \- Windows 10 ou Windows 11

# \- Windows PowerShell

# \- Permissão de Administrador

# \- Espaço disponível para operações temporárias

# 

# O programa solicita automaticamente privilégios de administrador através do UAC.

# 

# 

# COMO EXECUTAR

# ==============================================

# 

# Coloque os arquivos do projeto na mesma pasta:

# 

# Otimizador\_de\_PC.ps1

# Abrir-Otimizador.bat

# 

# O método recomendado para iniciar o programa é executar:

# 

# Abrir-Otimizador.bat

# 

# O arquivo BAT inicia o PowerShell utilizando a política:

# 

# \-ExecutionPolicy Bypass

# 

# Essa configuração é aplicada somente à sessão utilizada para executar o script e não altera permanentemente a política de execução do Windows.

# 

# Depois de executar o arquivo:

# 

# 1\. Aceite a solicitação do UAC.

# 2\. Aguarde o carregamento do programa.

# 3\. Escolha uma opção no menu principal.

# 

# 

# MENU PRINCIPAL

# ==============================================

# 

# O programa possui o seguinte menu:

# 

# \[0] ANALISAR PC

# \[1] OTIMIZAÇÃO INTELIGENTE

# \[2] VERSÃO PADRÃO

# \[3] VERSÃO GAMER

# \[4] DEBLOAT

# \[5] MANUTENÇÃO DO WINDOWS

# \[6] BENCHMARK

# \[7] REVERTER ÚLTIMA OTIMIZAÇÃO

# \[8] GERENCIAR INICIALIZAÇÃO

# \[9] SAIR

# 

# 

# 0 — ANALISAR PC

# ==============================================

# 

# A opção Analisar PC é uma ferramenta de diagnóstico.

# 

# Ela não foi criada para realizar alterações no sistema.

# 

# O programa coleta informações do computador e apresenta um diagnóstico contendo:

# 

# \- Sistema operacional

# \- Versão e build do Windows

# \- Tempo de atividade do sistema

# \- Processador

# \- Uso atual da CPU

# \- Memória RAM

# \- Memória RAM utilizada

# \- Memória RAM disponível

# \- Placa de vídeo

# \- Discos

# \- Espaço livre

# \- Tipo de armazenamento

# \- Reinicialização pendente

# \- Status do Windows Defender

# \- Ponto de restauração

# \- Programas de inicialização

# \- Windows Search

# \- TRIM

# \- Modo de Jogo

# 

# 

# PC HEALTH SCORE

# ==============================================

# 

# O programa possui um sistema de pontuação chamado PC Health Score.

# 

# A pontuação varia de 0 a 100 e serve como um indicador geral da situação atual do computador.

# 

# A pontuação considera diferentes informações do sistema, como:

# 

# \- Utilização da CPU

# \- Utilização da RAM

# \- Espaço livre no disco

# \- Quantidade de programas na inicialização

# \- Reinicialização pendente

# \- Proteção em tempo real

# \- Ponto de restauração

# 

# Classificação:

# 

# 80–100 = BOM

# 

# 50–79 = ATENÇÃO

# 

# 0–49 = CRÍTICO

# 

# IMPORTANTE:

# 

# O PC Health Score é apenas uma estimativa baseada nos indicadores coletados pelo programa.

# 

# Ele não representa um benchmark profissional e não determina sozinho se um computador é saudável ou rápido.

# 

# 

# 1 — OTIMIZAÇÃO INTELIGENTE

# ==============================================

# 

# A Otimização Inteligente analisa o computador antes de aplicar os ajustes.

# 

# O programa verifica informações como:

# 

# \- Tipo de armazenamento

# \- Quantidade de memória RAM

# \- Programas instalados

# \- Perfil provável de utilização do computador

# 

# O perfil pode ser identificado como:

# 

# \- Geral

# \- Trabalho

# \- Jogos

# \- Misto

# 

# A identificação do perfil serve para orientar a otimização.

# 

# A Otimização Inteligente pode executar:

# 

# \- Limpeza de arquivos temporários

# \- Limpeza de cache

# \- Limpeza da Lixeira

# \- Limpeza do cache DNS

# \- Otimização/TRIM das unidades

# \- Ajuste do plano de energia

# \- Ativação do Modo de Jogo em perfis com jogos

# \- Verificação dos arquivos do sistema

# 

# A verificação do sistema utiliza:

# 

# sfc /verifyonly

# 

# A Otimização Inteligente solicita confirmação antes de aplicar as alterações.

# 

# 

# 2 — VERSÃO PADRÃO

# ==============================================

# 

# A Versão Padrão foi criada para manutenção e otimização geral do computador.

# 

# Entre as operações realizadas estão:

# 

# LIMPEZA DE TEMPORÁRIOS

# \- Limpeza da pasta TEMP do usuário

# \- Limpeza da pasta TEMP do Windows

# \- Limpeza de arquivos temporários

# 

# LIXEIRA

# \- Esvaziamento da Lixeira

# 

# WINDOWS UPDATE

# \- Limpeza do cache de downloads do Windows Update

# 

# MINIATURAS

# \- Limpeza do cache de miniaturas do Explorer

# 

# CACHE DE NAVEGADORES

# \- Limpeza de cache do Chrome

# \- Limpeza de cache do Edge

# \- Limpeza de cache do Firefox

# 

# IMPRESSÃO

# \- Limpeza da fila de impressão

# 

# DISCO

# \- Otimização/TRIM das unidades

# 

# DNS

# \- Limpeza do cache DNS

# 

# ENERGIA

# \- Ativação do plano Alto Desempenho

# 

# LIMPEZA DO WINDOWS

# \- Execução da ferramenta nativa de limpeza do Windows

# 

# 

# 3 — VERSÃO GAMER

# ==============================================

# 

# A Versão Gamer inclui recursos da Versão Padrão e adiciona ajustes direcionados para computadores utilizados para jogos.

# 

# Entre os ajustes estão:

# 

# \- Plano de energia de Desempenho Máximo

# \- Ajustes de prioridade do sistema

# \- Ajustes de multimídia

# \- Ajustes de rede

# \- Game DVR

# \- Xbox Game Bar

# \- Modo de Jogo

# \- Hardware Accelerated GPU Scheduling (HAGS)

# \- Ajustes de efeitos visuais

# \- SysMain

# \- Windows Search

# \- Limpeza de cache DNS

# \- Otimização das unidades

# 

# Alguns ajustes podem depender do hardware, dos drivers e da versão do Windows instalada.

# 

# Algumas alterações podem exigir reinicialização.

# 

# 

# IMPORTANTE SOBRE A VERSÃO GAMER

# ==============================================

# 

# Os ajustes Gamer não garantem aumento de FPS.

# 

# O desempenho de cada jogo depende de diversos fatores:

# 

# \- Processador

# \- Placa de vídeo

# \- Memória RAM

# \- SSD/HDD

# \- Drivers

# \- Temperatura

# \- Configurações gráficas

# \- Jogo utilizado

# \- Versão do Windows

# 

# O objetivo da versão Gamer é aplicar configurações voltadas para desempenho, e não garantir uma quantidade específica de FPS ou redução de ping.

# 

# 

# 4 — DEBLOAT

# ==============================================

# 

# O Debloat é o modo mais agressivo do programa.

# 

# Ele permite remover aplicativos e desativar serviços, tarefas e recursos do Windows que podem permanecer executando em segundo plano.

# 

# O programa permite confirmar as etapas antes da execução.

# 

# 

# O DEBLOAT PODE TRABALHAR COM:

# 

# \- Serviços de telemetria

# \- Serviços de diagnóstico

# \- Cortana

# \- Widgets

# \- Windows Web Experience

# \- OneDrive

# \- Serviço de impressão

# \- Bluetooth

# \- Fax

# \- Remote Registry

# \- Remote Assistance

# \- Serviço de mapas

# \- Serviços adicionais de diagnóstico

# \- Tarefas agendadas relacionadas à telemetria e diagnóstico

# \- Aplicativos provisionados

# \- Efeitos visuais

# \- Windows Search

# 

# 

# XBOX

# ==============================================

# 

# O Xbox não é removido nem desativado diretamente pelo Debloat.

# 

# Recursos relacionados ao Game DVR e Xbox Game Bar podem ser tratados separadamente pela Versão Gamer.

# 

# 

# ATENÇÃO AO UTILIZAR O DEBLOAT

# ==============================================

# 

# O Debloat pode desativar recursos que são utilizados pelo usuário.

# 

# Por exemplo:

# 

# \- Impressoras podem deixar de funcionar.

# \- Bluetooth pode ser desativado.

# \- A pesquisa do Windows pode ser afetada.

# \- OneDrive pode ser removido.

# \- Recursos de diagnóstico podem ser desativados.

# \- Aplicativos do Windows podem ser removidos.

# 

# Antes de utilizar o Debloat em computadores de trabalho ou empresariais, verifique quais recursos são utilizados.

# 

# 

# 5 — MANUTENÇÃO DO WINDOWS

# ==============================================

# 

# O menu de Manutenção do Windows reúne ferramentas nativas para diagnóstico, verificação e reparo.

# 

# Opções:

# 

# \[1] Verificar arquivos do sistema (SFC)

# \[2] Reparar imagem do Windows (DISM)

# \[3] Verificar disco (CHKDSK)

# \[4] Verificar Windows Update

# \[5] Limpar componentes antigos

# \[6] Verificar integridade

# \[7] Voltar

# 

# 

# SFC

# ==============================================

# 

# O comando:

# 

# sfc /scannow

# 

# utiliza o System File Checker para verificar arquivos protegidos do Windows e tentar reparar arquivos corrompidos.

# 

# Também existe a opção:

# 

# sfc /verifyonly

# 

# Essa opção realiza a verificação sem executar o reparo.

# 

# 

# DISM

# ==============================================

# 

# O comando:

# 

# DISM /Online /Cleanup-Image /RestoreHealth

# 

# verifica e tenta reparar a imagem do Windows.

# 

# Essa operação pode levar vários minutos dependendo do computador.

# 

# 

# CHKDSK

# ==============================================

# 

# O programa utiliza:

# 

# chkdsk C: /scan

# 

# para realizar uma verificação online do sistema de arquivos.

# 

# Dependendo do problema encontrado, outras verificações podem exigir uma reinicialização.

# 

# 

# WINDOWS UPDATE

# ==============================================

# 

# A ferramenta de manutenção pode verificar:

# 

# \- Estado do serviço Windows Update

# \- Atualizações instaladas recentemente

# \- Possível reinicialização pendente

# 

# 

# LIMPAR COMPONENTES ANTIGOS

# ==============================================

# 

# O comando utilizado é:

# 

# DISM /Online /Cleanup-Image /StartComponentCleanup

# 

# Essa operação realiza uma limpeza do armazenamento de componentes do Windows, removendo versões antigas que não são mais necessárias.

# 

# 

# VERIFICAÇÃO DE INTEGRIDADE

# ==============================================

# 

# A opção de verificação executa:

# 

# sfc /verifyonly

# 

# e:

# 

# DISM /Online /Cleanup-Image /ScanHealth

# 

# Esses comandos são utilizados para verificar a integridade dos componentes do sistema.

# 

# Caso sejam encontrados problemas, podem ser utilizados os comandos de reparo.

# 

# 

# 6 — BENCHMARK

# ==============================================

# 

# O Benchmark realiza uma análise básica do desempenho atual do computador.

# 

# São coletadas informações como:

# 

# \- Uso da CPU

# \- Uso da RAM

# \- Velocidade de leitura do disco

# \- Velocidade de escrita do disco

# \- Espaço utilizado

# \- Tempo de inicialização do Windows

# 

# 

# TESTE DE DISCO

# ==============================================

# 

# O programa cria temporariamente um arquivo de teste de aproximadamente:

# 

# 100 MB

# 

# Depois realiza testes de:

# 

# \- Escrita

# \- Leitura

# 

# Após o teste, os arquivos temporários são removidos.

# 

# 

# TEMPO DE BOOT

# ==============================================

# 

# Quando os dados estão disponíveis nos registros de diagnóstico do Windows, o programa apresenta o tempo registrado da última inicialização.

# 

# 

# COMPARAÇÃO DE RESULTADOS

# ==============================================

# 

# Os resultados do benchmark são armazenados em:

# 

# %ProgramData%\\OtimizadorPC\\Snapshots\\benchmark.json

# 

# Um novo benchmark pode ser comparado com o resultado anterior.

# 

# Exemplo:

# 

# Boot:

# 18.2s → 15.7s

# 

# RAM:

# 42% → 37%

# 

# Leitura:

# Resultado anterior → Resultado atual

# 

# Escrita:

# Resultado anterior → Resultado atual

# 

# 

# IMPORTANTE SOBRE O BENCHMARK

# ==============================================

# 

# O Benchmark é uma ferramenta básica de referência.

# 

# Ele não é um benchmark profissional.

# 

# Os resultados podem variar dependendo de:

# 

# \- Processos em segundo plano

# \- Temperatura

# \- Cache

# \- Estado do disco

# \- Atualizações

# \- Drivers

# \- Carga do sistema

# 

# O benchmark também não representa diretamente o desempenho em jogos.

# 

# 

# 7 — REVERTER ÚLTIMA OTIMIZAÇÃO

# ==============================================

# 

# O programa possui um sistema de snapshot para registrar configurações antes de determinadas alterações.

# 

# Os snapshots são armazenados em:

# 

# %ProgramData%\\OtimizadorPC\\Snapshots\\

# 

# O arquivo principal utilizado é:

# 

# ultima\_otimizacao.json

# 

# O sistema pode registrar configurações como:

# 

# \- Registro do Windows

# \- Serviços

# \- Plano de energia

# \- Tarefas agendadas

# \- Outras configurações específicas

# 

# A opção Reverter tenta restaurar as configurações registradas anteriormente.

# 

# 

# PONTO DE RESTAURAÇÃO

# ==============================================

# 

# Antes das principais otimizações, o programa também tenta criar um Ponto de Restauração do Windows.

# 

# Isso adiciona uma camada extra de segurança.

# 

# Entretanto, o funcionamento do ponto de restauração depende das configurações do próprio Windows.

# 

# 

# LIMITAÇÕES DA REVERSÃO

# ==============================================

# 

# Nem todas as alterações podem ser revertidas automaticamente.

# 

# Alterações consideradas irreversíveis, como determinados aplicativos removidos, podem exigir:

# 

# \- Reinstalação do aplicativo

# \- Microsoft Store

# \- Instalador oficial

# \- Restauração do Sistema

# 

# 

# 8 — GERENCIAR INICIALIZAÇÃO

# ==============================================

# 

# O Gerenciador de Inicialização permite visualizar programas configurados para iniciar automaticamente com o Windows.

# 

# O programa apresenta informações como:

# 

# \- Nome

# \- Caminho

# \- Comando

# \- Escopo

# \- Impacto estimado

# 

# Opções:

# 

# \[1] Desativar itens

# \[2] Reativar itens

# \[3] Ver detalhes

# \[4] Voltar

# 

# 

# DESATIVAR PROGRAMAS

# ==============================================

# 

# Desativar um item da inicialização apenas impede que sua entrada de inicialização automática seja executada.

# 

# O programa não é desinstalado.

# 

# Antes da alteração, o sistema cria um backup.

# 

# 

# BACKUPS

# ==============================================

# 

# Os backups relacionados à inicialização são armazenados em:

# 

# %ProgramData%\\OtimizadorPC\\Backups\\

# 

# 

# SISTEMA DE SNAPSHOTS

# ==============================================

# 

# Os dados utilizados pelo sistema são armazenados em:

# 

# %ProgramData%\\OtimizadorPC\\

# 

# Estrutura:

# 

# %ProgramData%\\OtimizadorPC\\

# |

# +-- Snapshots\\

# |   |

# |   +-- ultima\_otimizacao.json

# |   +-- benchmark.json

# |

# +-- Backups\\

# 

# Esses arquivos são utilizados pelo programa durante as operações de backup, reversão e benchmark.

# 

# 

# INTERFACE

# ==============================================

# 

# O programa possui uma interface de terminal personalizada.

# 

# Entre os recursos visuais estão:

# 

# \- Dashboard do sistema

# \- Menu interativo

# \- Bordas personalizadas

# \- Cores por nível de status

# \- Indicadores de progresso

# \- PC Health Score

# \- Barras de pontuação

# \- Status de execução

# \- Contadores de operações

# \- Mensagens de sucesso

# \- Mensagens de alerta

# \- Mensagens de erro

# 

# 

# STATUS DAS OPERAÇÕES

# ==============================================

# 

# Durante a execução, o programa informa o resultado das etapas.

# 

# Exemplos:

# 

# \[OK]

# Operação executada com sucesso.

# 

# \[!!]

# A operação apresentou uma falha ou não se aplica ao computador.

# 

# \[--]

# Informação ou etapa que não exige alteração.

# 

# 

# SEGURANÇA

# ==============================================

# 

# O programa foi desenvolvido para tentar reduzir riscos durante as alterações.

# 

# Antes das principais otimizações, o programa pode:

# 

# 1\. Criar um ponto de restauração.

# 2\. Registrar configurações anteriores.

# 3\. Criar snapshots.

# 4\. Exibir as operações realizadas.

# 5\. Informar falhas durante a execução.

# 

# Mesmo assim, nenhum script de otimização pode garantir que todas as alterações serão reversíveis em qualquer computador.

# 

# 

# AVISO IMPORTANTE

# ==============================================

# 

# Este programa realiza alterações no Windows.

# 

# Dependendo da opção utilizada, ele pode modificar:

# 

# \- Registro do Windows

# \- Serviços

# \- Tarefas agendadas

# \- Aplicativos

# \- Configurações de energia

# \- Configurações de rede

# \- Recursos do Windows

# \- Programas de inicialização

# \- Arquivos temporários

# 

# Utilize por sua conta e risco.

# 

# O modo Debloat e alguns ajustes da Versão Gamer são mais agressivos e devem ser utilizados com atenção.

# 

# 

# PROBLEMAS COM CARACTERES

# ==============================================

# 

# Se aparecerem erros como:

# 

# The string is missing the terminator

# Unexpected token

# Missing closing

# 

# ou caracteres estranhos no terminal, verifique a codificação do arquivo .ps1.

# 

# Para o Windows PowerShell 5.1, recomenda-se salvar o script como:

# 

# UTF-8 with BOM

# 

# 

# NO VS CODE

# ==============================================

# 

# No VS Code:

# 

# Salvar com Codificação

# → UTF-8 with BOM

# 

# Isso é importante porque o programa utiliza caracteres especiais na interface.

# 

# 

# SE A JANELA FECHAR SOZINHA

# ==============================================

# 

# Verifique:

# 

# \- Se o .BAT e o .PS1 estão na mesma pasta.

# \- Se o UAC foi aceito.

# \- Se o antivírus bloqueou o arquivo.

# \- Se o script está salvo corretamente.

# \- Se a codificação do arquivo está correta.

# 

# Também é possível executar manualmente através do PowerShell como Administrador:

# 

# cd "C:\\Caminho\\Do\\Projeto"

# 

# Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

# 

# .\\Otimizador\_de\_PC.ps1

# 

# 

# REINICIAR O COMPUTADOR

# ==============================================

# 

# Após algumas otimizações, recomenda-se reiniciar o computador.

# 

# Isso é especialmente importante após alterações relacionadas a:

# 

# \- GPU Scheduling

# \- Serviços

# \- Registro

# \- Plano de energia

# \- Componentes do Windows

# \- Drivers

# \- Configurações do sistema

# 

# Nem toda operação exige reinicialização.

# 

# 

# FILOSOFIA DO PROJETO

# ==============================================

# 

# O objetivo do Otimizador de PC não é simplesmente desativar o máximo possível de serviços.

# 

# A proposta é oferecer diferentes níveis de intervenção:

# 

# ANALISAR

# &#x20;   ↓

# ENTENDER O ESTADO DO PC

# &#x20;   ↓

# ESCOLHER O NÍVEL DE OTIMIZAÇÃO

# &#x20;   ↓

# CRIAR BACKUP / RESTAURAÇÃO

# &#x20;   ↓

# APLICAR ALTERAÇÕES

# &#x20;   ↓

# VERIFICAR RESULTADOS

# &#x20;   ↓

# REVERTER SE NECESSÁRIO

# 

# 

# OBSERVAÇÕES

# ==============================================

# 

# \- Algumas funções dependem da versão do Windows.

# \- Alguns serviços podem não existir em determinadas edições.

# \- Alguns recursos dependem do hardware e dos drivers instalados.

# \- HAGS depende do suporte do hardware e do driver.

# \- O plano Ultimate Performance pode não estar disponível nativamente em todos os sistemas.

# \- O Windows pode impedir determinadas alterações dependendo das políticas configuradas.

# \- Algumas operações exigem privilégios administrativos.

# \- O benchmark não representa necessariamente o desempenho em jogos.

# \- O PC Health Score é uma estimativa.

# \- Recomenda-se realizar backups importantes antes de utilizar ferramentas de manutenção do sistema.

# \- Recomenda-se revisar as opções antes de executar o Debloat em computadores empresariais.

# 

# 

# ESTRUTURA DO PROJETO

# ==============================================

# 

# Otimizador-PC/

# |

# +-- Otimizador\_de\_PC.ps1

# +-- Abrir-Otimizador.bat

# +-- README.txt

# 

# 

# DADOS GERADOS

# ==============================================

# 

# Durante a utilização, o programa pode criar:

# 

# %ProgramData%\\OtimizadorPC\\Snapshots\\

# 

# e:

# 

# %ProgramData%\\OtimizadorPC\\Backups\\

# 

# Esses diretórios são utilizados para armazenar informações temporárias, snapshots, benchmarks e backups necessários para o funcionamento das ferramentas.

# 

# 

# DIREITOS AUTORAIS

# ==============================================

# 

# © 2026 Pedro Queiroz — Todos os direitos reservados.

# 

# Este projeto e seu código são de propriedade de Pedro Queiroz.

# 

# Softwares, comandos, componentes e ferramentas de terceiros utilizados pelo projeto permanecem sujeitos às suas respectivas licenças e termos de uso.

# 

# 

# AUTOR

# ==============================================

# 

# Pedro Queiroz

# 

# Projeto desenvolvido para manutenção, diagnóstico e otimização de computadores Windows utilizando PowerShell.

# 

# 

# FIM

# ==============================================

