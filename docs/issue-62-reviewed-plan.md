# Revisão técnica independente — Issue #62 (V7.P12)

## Resultado executivo

**SHA remoto analisado na branch `main`:** `442a319cec34c169951b0a9013dd4c13ffb24e14`.

A consulta de branches e commits confirmou esse SHA em `main`. O checkout local estava limpo nesse mesmo SHA, embora na branch `copilot/investigate-issue-62-plan`.

No estado remoto analisado, não encontrei implementação do Coordination Broker nem do `owner-auth.json`. A arquitetura Option B está aprovada, mas sua execução e os testes exigidos não estão demonstrados. O checkpoint canônico `#6664250173` mantém a Slice 02 pendente e o handoff em `NO_GO`.

## Evidências e divergência de estado

- A referência histórica `718d89a33b74263ed827d2c1f97ddd40dcc0c258` está na história de `main`. Desde ela, o remoto avançou com `ebc1b785` (documentação REL4) e `442a319c` (updater de worktrees Windows), não com commits de P12.
- A issue #62 continua aberta. Seu objetivo original é scheduling/concurrency; o checkpoint mais recente a direciona para aceitação de segurança cross-platform da Slice 02.
- O CI mais recente da `main`, run `41902850327`, passou em `windows-2025` e `ubuntu-24.04`. Ele executa restore, build, testes gerais e pack — não prova isolamento de identidades ou segurança do Broker.
- A árvore remota em `/home/runner/work/AI.RepoKit.MCP/AI.RepoKit.MCP/src/AiRepoKit.WorkflowExecution` contém componentes de checkpoint, gate, repair e evidence pack, mas não Broker. A busca no GitHub por `WorkflowCoordinationBroker` e `owner-auth.json` também não encontrou resultados.
- A configuração em `/home/runner/work/AI.RepoKit.MCP/AI.RepoKit.MCP/.github/workflows/ci.yml` tem uma matriz Windows/Ubuntu, sem configuração visível de usuários/SIDs distintos ou testes adversariais multiprocessos.
- O checkpoint aprova Option B, preserva o layout IPC `<storageRoot>/ipc/broker.sock` e `owner-auth.json` schema v1, mas não aceita a Slice 02. As correções NTFS locais v5 não aparecem neste checkout limpo; não as considero disponíveis nem verificadas.
- Há comentários históricos que registram falhas em implementações anteriores do Broker. Como o código correspondente não está no remoto examinado, trato esses registros como pistas de revisão, não como prova do estado atual.

## Matriz de requisitos

| Requisito | Estado | Base da avaliação |
|---|---|---|
| `main` remoto e divergência do checkpoint | **Comprovado** | SHA remoto acima; commits posteriores ao baseline são REL4, sem implementação P12 identificada. |
| Option B e `storageRoot` pré-provisionado protegido pelo SO | **Comprovado como decisão; não implementado/verificado** | Checkpoint #6664250173. |
| Broker com identidade isolada dos workers | **Pendente** | Não há Broker no código remoto examinado. |
| Autenticação IPC mútua e operações privilegiadas vinculadas à sessão/geração | **Não verificável** | Não há implementação IPC para inspecionar. |
| Proteção do endpoint e do `owner-auth.json` v1 | **Não verificável** | Schema/layout fazem parte da decisão, mas os arquivos e controles não estão no remoto. |
| Preservação P01–P11 e dos seis hashes da Slice 01 | **Não verificável nesta revisão** | O checkpoint exige preservação; não recalculei os seis hashes nem validei toda a superfície pública. |
| Metadados de coordenação protegidos pelo SO; persistência P02/P09 cooperativa | **Comprovado como limite arquitetural** | Checkpoint #6664250173; não há prova de implementação. |
| Crash recovery, quarentena durável e exclusão de schedulers legados | **Pendente** | Não há Broker para testar ou revisar. |
| Reconciliação P09 e manutenção offline fail-closed | **Pendente** | Não há fluxo Broker de manutenção/reconciliação no remoto. |
| Windows cross-SID, Ubuntu cross-UID e ataques multiprocessos | **Pendente** | CI atual não mostra esses testes; o checkpoint os exige. |
| Build/teste geral Windows e Ubuntu | **Comprovado para o SHA do run #131** | CI passou; isso não equivale a aceite da Slice 02. |
| Disponibilidade das correções NTFS locais v5 | **Não verificável/indisponível neste checkout** | Checkout limpo no SHA remoto analisado. |

## Fluxo e controles que ainda precisam de prova

O checkpoint exige que o Broker seja a autoridade de coordenação, separado dos workers. A implementação deverá comprovar, em ambos os sentidos, que o cliente está falando com o Broker autorizado e que o Broker reconhece o principal do worker. A identidade esperada não pode ser inferida apenas da identidade do processo cliente. Operações privilegiadas devem ficar vinculadas à sessão autenticada e à geração do owner.

O Broker deverá rejeitar configuração insegura do `storageRoot` antes de admitir workers, proteger hierarquia, metadados, ownership e endpoint por ACL/permissões do SO, e preservar os caminhos e o schema aprovados. `owner-auth.json` não pode ser tratado como prova criptográfica de origem. P02/P09 continuam cooperativos: não se deve alegar que o Broker impede fisicamente toda escrita posterior do worker nesses dados.

Nada disso pode ser confirmado na implementação remota porque ela não está presente.

## Plano ordenado até o possível aceite

1. **Fixar a baseline de trabalho:** partir do `main` remoto `442a319c`; registrar os seis hashes e os contratos P01–P11 contra a fonte canônica antes de integrar código. Não tratar os antigos resultados “6/6” reportados como verificação atual.
2. **Recuperar as correções NTFS v5:** solicitar o patch exato, paths, revisão e hashes. Não presumir que o patch esteja neste clone.
3. **Implementar o Broker sem expansão de contratos:** manter Option B, layout existente e schema v1; rejeitar `storageRoot` arbitrário/inseguro e impedir coexistência de Broker e scheduler legado no mesmo root.
4. **Fechar recuperação e fail-closed:** testar quedas em cada fronteira de publicação/lock; manter proteção se quarentena não puder ser publicada; reconciliar leases órfãs; não liberar manutenção sem evidência P09 completa; bloquear admissão quando o resultado for ambíguo.
5. **Adicionar testes de segurança isolados:** provar controles positivos e negativos de IPC, identidade, ACL/permissões, endpoint rogue, tampering e concorrência multiprocessos.
6. **Executar testes reais em hosts efêmeros:** Windows com processos sob SIDs distintos; Ubuntu 24.04 com UIDs distintos, no kernel Linux real. Capturar identidade efetiva dos processos e permissões observadas.
7. **Validar contratos e regressões:** build e suíte completa nas duas plataformas; confirmar os seis hashes e a superfície pública P01–P11 sem alterações.
8. **Revisão independente e evidência:** entregar inventário de paths, resultados por plataforma e marcar qualquer teste não executado como `NOT_EXECUTED`. Só então solicitar a decisão humana de aceite da Slice 02. P12 permanece separadamente sujeito ao seu escopo/aceite.

## Ambientes, CI e comandos

**Proposta para provas reais:** usar runners/VMs efêmeros e descartáveis, nunca o Windows do operador. No Windows, criar principals temporários e iniciar Broker e workers com tokens locais realmente distintos — não `runas /netonly` — aplicando ACLs ao root e endpoint. No Ubuntu, criar UID separado para Broker e workers, provisionar o root com permissões restritas e verificar acesso e rejeição de conexões. Coletar logs de identidade e ACL/modo sem gravar credenciais. A infraestrutura atual não faz esse provisionamento nem define esses testes.

Comandos existentes, a partir da raiz `/home/runner/work/AI.RepoKit.MCP/AI.RepoKit.MCP`:

```sh
dotnet restore /home/runner/work/AI.RepoKit.MCP/AI.RepoKit.MCP/AI.RepoKit.MCP.sln
dotnet build /home/runner/work/AI.RepoKit.MCP/AI.RepoKit.MCP/AI.RepoKit.MCP.sln -c Release --no-restore
dotnet test /home/runner/work/AI.RepoKit.MCP/AI.RepoKit.MCP/AI.RepoKit.MCP.sln -c Release --no-build
dotnet test /home/runner/work/AI.RepoKit.MCP/AI.RepoKit.MCP/tests/AiRepoKit.WorkflowExecution.Tests/AiRepoKit.WorkflowExecution.Tests.csproj -c Release
```

Esses comandos cobrem o que já existe, não os testes do Broker. O comando de integração cross-SID/cross-UID ainda precisa ser definido junto com os harnesses; não há teste/script correspondente no remoto examinado.

**Critérios objetivos de aceite:** autenticação positiva para principal autorizado e rejeição dos demais; identidade do Broker distinta e comprovada; workers sem permissão de alterar/remover metadados protegidos ou endpoint; rogue endpoint/tampering rejeitados; falha de quarentena mantém exclusão; crash/restart não admite trabalho até reconciliação segura; P09 ambíguo permanece fail-closed; testes multiprocessos Windows e Linux passam; seis hashes e contratos P01–P11 preservados; build e testes gerais passam em ambos os OS.

## Estratégia de PRs e NTFS

Proposta de PRs pequenos, em sequência após autorização:

1. **NTFS v5 isolado:** importar o patch exato como commit separado; comparar hunks e hashes antes/depois. Não substituir arquivos inteiros nem fazer checkout sobre mudanças existentes.
2. **Core do Broker:** admissão, ownership, sessões, recuperação/quarentena e testes determinísticos, sem novos tipos públicos ou schemas.
3. **Segurança Windows:** ACL/endpoint, cross-SID e integração da correção NTFS apenas onde houver sobreposição comprovada.
4. **Segurança Linux:** permissões/socket, cross-UID e multiprocessos no kernel Linux real.
5. **CI e pacote de evidências:** jobs de identidade efêmeros, comandos reproduzíveis e verificação final de hashes/contratos.

O patch NTFS deve ser guardado antes de qualquer rebase ou integração, aplicado por patch/3-way sobre a baseline compatível, e cada conflito resolvido por hunk com revisão. Se a correção tocar contratos ou paths congelados, parar e pedir nova decisão; não “resolver” sobrescrevendo a outra versão.

## Autorizações humanas e bloqueios

- **Autorização separada necessária:** instalação do serviço e provisionamento privilegiado do ambiente/ACLs. Mesmo em runner efêmero, qualquer configuração privilegiada precisa de aprovação apropriada.
- **Não autorizado pelo checkpoint:** mutar P01–P11, Slice 01, tipos públicos ou schemas adicionais; commit, branch, push, deploy, tag ou release.
- **Proibido nesta sessão:** quaisquer mutações, acesso/modificação do host Windows do operador, alteração de worktrees/responsabilidades B/C ou início de P13.
- **Bloqueios técnicos atuais:** Broker e harnesses ausentes no remoto; patch NTFS v5 ausente no checkout; seis hashes ainda precisam de verificação independente; CI não provisiona principals distintos nem coleta evidência cross-principal.

## Status finais obrigatórios

```text
SLICE_02_ACCEPTED=NO
P12_COMPLETE=NO
P13_AUTHORIZED=NO
FRONT_A_HANDOFF=NO_GO
```
