# TODO

- Validar em ambiente Linux real (fora de container) permissões de `/proc/<pid>/mem` com diferentes políticas (`ptrace_scope`/AppArmor) e decidir se o fallback por logs deve virar padrão para paridade segura.
- Implementar notificações nativas e estratégia de autoatualização no cliente Linux para fechar 100% da paridade funcional com o cliente WPF.
- Capturar screenshots oficiais dos dois clientes (WPF e Linux Avalonia) para atualizar README e documentação visual.
- Consolidar o fallback de resolução de diretório em `SteamEnvironment` para usar apenas `ISteamInstallPathProvider`, evitando duplicação de heurísticas fora do Infrastructure.
- Reutilizar a nova lista de candidatos Linux no bootstrap do cliente Avalonia para tentativa de inicialização explícita do `ISteamClientAdapter` no startup.

- Criar testes automatizados para o fluxo de atualização Linux cobrindo erro de rede, hash inválido e rollback de substituição do AppImage.
- Evoluir notificação Linux para implementação DBus direta (sem dependência de `notify-send`) para maior controle de timeout/categorias.
- Automatizar a geração do relatório de paridade a partir dos resultados TRX (em vez de tabela declarativa) para destacar diferenças de cobertura entre suites automaticamente.

