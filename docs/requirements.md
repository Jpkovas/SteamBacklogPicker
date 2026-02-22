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
1. **Seleção Rápida Noturna**: Ana abre o aplicativo, filtra por "tempo de jogo < 1h" e "jogos recém-adicionados", recebendo sugestões alinhadas ao seu humor relaxante.
2. **Planejamento de Final de Semana**: Bruno usa o desktop para listar jogos com 80% de conquistas concluídas, define metas e registra o progresso esperado para o fim de semana.
3. **Sessão Cooperativa com Amigos**: Carla filtra por jogos cooperativos da própria biblioteca e agenda a sessão.
4. **Curadoria de Promoções**: Usuário compara jogos em promoção com o backlog, destacando títulos com alta prioridade e custo-benefício.
5. **Revisão Mensal**: Relatório automático destaca jogos não tocados nos últimos 6 meses e sugere rotação para manter a biblioteca ativa.

## Paridade de funcionalidade (Windows x Linux)

A paridade é validada por jornada do usuário e deve ser mantida para qualquer release em ambas as plataformas.

| Jornada do usuário | Critério de aceitação | Windows | Linux |
| --- | --- | --- | --- |
| Abrir app e carregar biblioteca Steam | App abre sem erro e lista jogos locais da Steam em até 60s para biblioteca média | Obrigatório | Obrigatório |
| Aplicar filtros e refinar sorteio | Filtros principais (tags, coleção, instalado/não instalado) alteram o conjunto sorteável de forma equivalente | Obrigatório | Obrigatório |
| Sortear jogo e repetir sorteio | Ação de sorteio apresenta jogo válido e `Sortear novamente` responde sem travar | Obrigatório | Obrigatório |
| Abrir jogo no cliente Steam | Ação de abrir jogo dispara protocolo/comando Steam para título sorteado | Obrigatório | Obrigatório |
| Diagnóstico e logs | Logs locais incluem erros de descoberta e falhas de inicialização | Obrigatório | Obrigatório |
| Notificações de UX | Notificação de ação concluída existe (nativa ou fallback documentado) | Obrigatório | Obrigatório |
| Atualização do app | Fluxo de atualização deve existir; diferenças temporárias devem ser registradas no changelog de portabilidade | Obrigatório | Obrigatório (pode ser temporariamente diferente) |

### Regra de aceite de paridade
- Uma release só é considerada "com paridade" quando todas as jornadas acima estiverem com status equivalente ou diferença temporária registrada conforme a convenção abaixo.
- Diferenças temporárias precisam de prazo de convergência explícito e dono responsável.

## Convenção de changelog de portabilidade

Para registrar divergências temporárias entre plataformas, usar o bloco abaixo em `CHANGES.md`:

```markdown
- [Portabilidade] <jornada afetada>: <diferença temporária>
  - Impacto: <baixo|médio|alto>
  - Mitigação atual: <como o usuário contorna>
  - Convergência alvo: <AAAA-MM-DD>
  - Responsável: <time ou papel>
```

Regras:
- Sempre registrar a jornada impactada (mesma nomenclatura da tabela de paridade).
- Toda entrada precisa conter data-alvo de convergência (`Convergência alvo`).
- Ao convergir comportamento, adicionar nota curta de encerramento na release correspondente.

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
- **Preferências Persistidas**: Salvar filtros, layouts e critérios personalizados por usuário.
- **Suporte a Múltiplas Contas**: Permitir vincular mais de uma conta Steam, alternando ou agregando bibliotecas com permissões explícitas.
- **Acessibilidade**: Oferecer contraste adequado, suporte a leitores de tela e navegação via teclado/controller.
- **Onboarding**: Fluxo guiado inicial apresentando funcionalidades-chave e solicitando consentimento para leitura da biblioteca local.
- **Feedback Contínuo**: Superfície para sugestões e indicadores de sincronia ou erros de importação.

### Validação com Stakeholders
- Workshops com jogadores representando as personas para revisar protótipos de filtros e dashboards.
- Entrevistas com especialistas em UX e compliance para validar aderência aos termos da Steam.
- Testes de usabilidade remotos e presenciais medindo tempo para encontrar um jogo adequado.
- Revisões trimestrais com stakeholders internos (produto, engenharia, jurídico) para priorizar backlog de melhorias.
