# Requisitos do Steam Backlog Picker

## Objetivos do Aplicativo
- Permitir que jogadores organizem e priorizem seu backlog de jogos da Steam.
- Facilitar a descoberta de títulos esquecidos com base em critérios personalizados.
- Acompanhar progresso, status de conclusão e tempo restante estimado para cada jogo.
- Oferecer recomendações baseadas no humor, disponibilidade de tempo e preferências do jogador.

## Personas
1. **Ana, a Exploradora de Novidades**
   - 28 anos, grande biblioteca de jogos adquiridos em promoções.
   - Procura sugestões rápidas para sessões curtas após o trabalho.
   - Valoriza automação e dicas contextuais.
2. **Bruno, o Complecionista**
   - 35 anos, registra minuciosamente conquistas e estatísticas.
   - Deseja relatórios detalhados de progresso e metas semanais.
   - Espera integrações com conquistas Steam e alertas de pendências.
3. **Carla, a Jogadora Social**
   - 24 anos, alterna entre jogos solo e experiências cooperativas.
   - Quer sincronização com amigos, recomendando jogos compartilhados.
   - Preocupa-se em alinhar agendas e prefere aplicativos com suporte móvel.

## Filtros de Seleção
- Gênero, tags e categorias definidas na Steam.
- Tempo estimado de conclusão (HowLongToBeat, média pessoal, ou customizado).
- Status de propriedade (não iniciado, em andamento, concluído, abandonado).
- Disponibilidade de conquistas, cards colecionáveis ou suporte a controles.
- Compatibilidade com Steam Deck, Proton ou sistemas operacionais específicos.
- Jogos com avaliações positivas recentes ou lançamentos dentro de um período.
- Jogos apropriados ao humor ou energia atual (relaxante, competitivo, história).
- Modo de jogo (singleplayer, cooperativo local/online, multiplayer massivo).

## Cenários de Uso
1. **Seleção Rápida Noturna**: Ana abre o aplicativo no celular, filtra por "tempo de jogo < 1h" e "jogos recém-adicionados", recebendo sugestões alinhadas ao seu humor relaxante.
2. **Planejamento de Final de Semana**: Bruno usa o desktop para listar jogos com 80% de conquistas concluídas, define metas e registra o progresso esperado para o fim de semana.
3. **Sessão Cooperativa com Amigos**: Carla sincroniza a biblioteca com amigos, filtra por jogos cooperativos que todos possuem e agenda uma partida, enviando convites.
4. **Curadoria de Promoções**: Usuário compara jogos com desconto em promoção atual com o backlog, destacando títulos com alta prioridade e custo-benefício.
5. **Revisão Mensal**: Relatório automático destaca jogos não tocados nos últimos 6 meses e sugere rotação para manter a biblioteca ativa.

## Análise dos Termos de Uso da Valve/Steam
- **Referências**: Steam Subscriber Agreement (SSA), Steamworks SDK License Agreement e Steam Online Conduct Guidelines.
- **Acesso a Arquivos Locais e DLLs**:
  - O SSA permite que usuários executem e modifiquem conteúdos adquiridos para uso pessoal, mas restringe engenharia reversa e redistribuição.
  - Extração de dados diretamente dos arquivos de instalação ou DLLs pode violar cláusulas contra circumvenção de medidas técnicas ou acesso não autorizado.
  - O uso de APIs ou arquivos locais para obter dados deve respeitar limites impostos pelo cliente Steam e não interferir nos serviços da Valve.
- **Integração Local**:
  - Ler metadados de bibliotecas locais é aceitável quando não envolve contornar proteções DRM ou modificar executáveis.
  - Injetar ou carregar DLLs personalizados no cliente Steam é expressamente proibido sem autorização da Valve.
  - Automação que interfira no cliente (por exemplo, macros ou bots) pode resultar em banimento da conta.
- **Riscos Identificados**:
  - **Perda de Acesso**: Violações podem acarretar suspensão da conta Steam ou revogação de licenças.
  - **Implicações Legais**: Possíveis alegações de infração contratual ou violação de direitos autorais.
  - **Segurança**: Manuseio inadequado de DLLs pode introduzir vulnerabilidades ou ser interpretado como comportamento malicioso pelos sistemas antitrapaça.
  - **Conformidade**: Necessidade de auditoria das permissões e comunicação transparente com usuários sobre dados acessados.
- **Recomendações**:
  - Priorizar o uso das APIs oficiais (Steam Web API) e do Steamworks quando aplicável.
  - Solicitar orientações formais da Valve para recursos além do permitido explicitamente.
  - Implementar políticas de privacidade e consentimento claros ao acessar dados locais.

## Requisitos de UX e Validação
- **Idioma**: Interface multilíngue com prioridade para português e inglês; permitir alternância rápida e traduções revisadas por falantes nativos.
- **Preferências Persistidas**: Salvar filtros, layouts e critérios personalizados por usuário, sincronizando entre dispositivos quando possível.
- **Suporte a Múltiplas Contas**: Permitir vincular mais de uma conta Steam, alternando ou agregando bibliotecas com permissões explícitas.
- **Acessibilidade**: Oferecer contraste adequado, suporte a leitores de tela e navegação via teclado/controller.
- **Onboarding**: Fluxo guiado inicial apresentando funcionalidades-chave e solicitando consentimento para leitura da biblioteca local.
- **Feedback Contínuo**: Superfície para sugestões e indicadores de sincronia ou erros de importação.

### Validação com Stakeholders
- Workshops com jogadores representando as personas para revisar protótipos de filtros e dashboards.
- Entrevistas com especialistas em UX e compliance para validar aderência aos termos da Steam.
- Testes de usabilidade remotos e presenciais medindo tempo para encontrar um jogo adequado.
- Revisões trimestrais com stakeholders internos (produto, engenharia, jurídico) para priorizar backlog de melhorias.
- Coleta contínua de métricas (Net Promoter Score, retenção, taxa de recomendação aceita) para embasar decisões de roadmap.
