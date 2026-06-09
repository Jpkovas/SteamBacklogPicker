# TODO

- Provisionar a chave pública/privada de release para ativar assinatura do feed de update Linux no canal oficial.

- Executar validação manual em Linux real das permissões de `/proc/<pid>/mem` com diferentes políticas (`ptrace_scope`/AppArmor), mantendo o fallback/no-op como padrão seguro já implementado.
- Concluir estratégia de autoatualização no cliente Linux em ambiente de release para fechar a paridade funcional com o cliente WPF.
- Capturar screenshots oficiais dos dois clientes (WPF e Linux Avalonia) para atualizar README e documentação visual.
- Provisionar assinatura Authenticode/pinning de certificado para reativar auto-update Windows sem depender do opt-in legado `SBP_ENABLE_LEGACY_WINDOWS_UPDATE`.
