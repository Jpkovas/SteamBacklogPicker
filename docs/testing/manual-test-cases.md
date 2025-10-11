# Casos de Teste Manuais

Os cenários abaixo complementam a suíte automatizada e devem ser exercitados antes de cada entrega relevante. Eles focam em cenários críticos envolvendo autenticação, descoberta de bibliotecas Steam e comportamentos offline.

## 1. Conta Própria
- **Pré-condições:** Iniciar sessão com uma conta que possua jogos próprios instalados na biblioteca padrão da Steam.
- **Passos:**
  1. Iniciar o Steam Backlog Picker conectado à Internet.
  2. Forçar a sincronização inicial (menu `Biblioteca` → `Atualizar agora`).
  3. Verificar se a lista exibe todos os jogos instalados e identifica corretamente tempo jogado.
- **Critérios de sucesso:** Jogos próprios aparecem como instalados, sem marcações de compartilhamento e com metadados completos.

## 2. Somente Family Sharing
- **Pré-condições:** Utilizar uma conta sem jogos próprios, mas com acesso via Family Sharing a pelo menos dois títulos.
- **Passos:**
  1. Iniciar o aplicativo conectado à Internet.
  2. Executar a sincronização de bibliotecas.
  3. Verificar se os títulos compartilhados aparecem com o indicador de Family Sharing.
- **Critérios de sucesso:** Nenhum jogo é sinalizado como `não instalado`; todos os títulos disponíveis via compartilhamento aparecem com a flag de acesso compartilhado.

## 3. Múltiplas Bibliotecas
- **Pré-condições:** Configurar três pastas de biblioteca Steam (SSD interno, HDD secundário, armazenamento externo) com jogos distribuídos entre elas.
- **Passos:**
  1. Iniciar o aplicativo.
  2. Confirmar nas configurações que todas as bibliotecas foram detectadas.
  3. Validar que cada jogo aponta para o caminho físico correto e que filtros por localização funcionam.
- **Critérios de sucesso:** Nenhuma biblioteca fica ausente, caminhos exibidos corretamente e filtros por biblioteca retornam resultados consistentes.

## 4. Modo Offline
- **Pré-condições:** Colocar o Steam em modo offline antes de iniciar o aplicativo.
- **Passos:**
  1. Abrir o Steam Backlog Picker.
  2. Validar que o cache local é utilizado (sem chamadas de rede inesperadas).
  3. Forçar uma atualização manual e observar mensagens de fallback.
- **Critérios de sucesso:** Aplicativo permanece responsivo, utiliza dados do cache e mostra feedback claro sobre limitações do modo offline.

> **Observação:** Registrar evidências (capturas de tela ou vídeo) para cada execução manual e anexar ao repositório interno de QA.
