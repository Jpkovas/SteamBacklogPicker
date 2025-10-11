# Checklist de Regressão

Execute os itens abaixo antes de liberar uma nova versão.

## Automação
- [ ] Rodar `scripts/local-pipeline.sh` (aceita parâmetros extras como `-c Release`).
- [ ] Verificar que todos os testes unitários/integrados passam sem warnings novos relevantes.
- [ ] Atualizar o relatório de desempenho caso haja alterações significativas na inicialização.

## Verificações Manuais
- [ ] Revisar os cenários descritos em [Casos de Teste Manuais](./manual-test-cases.md).
- [ ] Validar acessibilidade básica (navegação por teclado e leitores de tela) na UI principal.
- [ ] Confirmar que novas dependências estão documentadas no `README.md`.

## Auditoria
- [ ] Checar se há migrações ou scripts adicionais e anexá-los ao pipeline local.
- [ ] Garantir que mudanças em configurações sensíveis (Steam API, caminhos de biblioteca) possuam rollback documentado.
- [ ] Registrar evidências (logs, capturas) no repositório interno de QA.
