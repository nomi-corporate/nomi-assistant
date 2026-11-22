# Interpretação de compromissos — ativação no ambiente de desenvolvimento

A rota `POST /api/v1/commitment-proposals` implementa um subconjunto da TEC-003 §7 para o MVP. O texto do estudante é interpretado pelo Claude Haiku 5.5 pela API da Anthropic, com saídas estruturadas em JSON Schema e esforço `low`. A saída da IA é validada pelo `CommitmentProposalBuilder` antes de virar proposta.

A rota nunca cria compromissos: o Mobile envia a proposta revisada ao Academic, que revalida os dados. O áudio não passa pelo Assistant. A voz é transcrita no próprio celular, e só o texto chega a esta rota.

## Comportamento

- **Fora do tema acadêmico:** status `unsupported`, sem candidato, com mensagem para o usuário. Exemplos: compras, saúde, lazer ou pedidos ao assistente. Uma recusa de segurança do modelo recebe o mesmo tratamento.
- **Matéria e professor:** são lidos no Academic (`/api/v1/subjects` e `/api/v1/professors`) com o token do próprio usuário. A proposta só usa IDs que pertencem a ele. Quando a fala cita um professor que leciona uma única matéria, essa matéria é vinculada automaticamente. Se ele leciona várias, a proposta pergunta qual.
- **Data ausente, passada ou inválida:** status `needsClarification`, com pergunta. Um horário que não existe no fuso (horário de verão) também gera pergunta.
- **Limite de uso:** 30 interpretações a cada 10 minutos por usuário. Acima disso, responde 429. O limite fica em memória e vale por instância.
- **Indisponibilidade:** sem chave, ou com a API da Anthropic fora do ar, responde 503 e o Mobile abre o formulário manual. O texto não é registrado em log.
- **Fora do MVP:** as propostas não são persistidas. `messages` (esclarecimento) e `DELETE` ficam para depois. O `Idempotency-Key` é aceito, mas não é armazenado.

## Configuração

| Variável | Onde | Valor |
| --- | --- | --- |
| `Anthropic__ApiKey` | `~/nomi/env/assistant.env` (segredo, permissão 600) | chave criada em platform.claude.com |
| `Academic__BaseUrl` | `environment` do `compose.yaml` | `https://nomi-dev-equipe.duckdns.org/academic` |
| `Interpretation__ModelId` | `environment` do `compose.yaml` | `claude-haiku-5-5` (padrão do código) |
| `Interpretation__Enabled` | opcional | `false` desliga a rota (503) |

A chave nunca vai para o Git, o Mobile ou os logs. Crie uma chave exclusiva para o ambiente de desenvolvimento e defina um limite de gasto no Console da Anthropic.

## Passos

1. Em platform.claude.com, crie uma organização ou workspace, adicione créditos pré-pagos e crie uma chave de API para o Assistant de desenvolvimento.
2. Na EC2, acrescente `Anthropic__ApiKey=<chave>` ao `~/nomi/env/assistant.env`, digitando a chave na sessão SSM (não cole em chats nem em tickets).
3. Publique a imagem pelo CI, atualize o digest no `release.env` e rode `compose.py up assistant`.

## Teste com o modelo real

Os testes `ClaudeInterpreterLiveTests` só chamam o modelo com `NOMI_CLAUDE_LIVE=1` e `ANTHROPIC_API_KEY` definidos:

```bash
NOMI_CLAUDE_LIVE=1 ANTHROPIC_API_KEY=... dotnet test tests/Nomi.Assistant.Tests --filter "FullyQualifiedName~Live"
```

## Custo de referência

O Haiku 5.5 custa US$ 0,10 por milhão de tokens de entrada e US$ 0,50 por milhão de tokens de saída, para prompts de até 100 mil tokens. Cada interpretação usa cerca de 1.000 tokens de entrada e algumas centenas de saída, o que dá frações de centavo por chamada. Confira a tabela de preços da Anthropic antes de ampliar o uso.

## Histórico

Até 22/11/2026, a interpretação usava o Amazon Bedrock. A pilha IAM `nomi-dev-interpretation` (`bedrock:InvokeModel`) deixou de ser usada e pode ser removida pelo responsável da conta.
