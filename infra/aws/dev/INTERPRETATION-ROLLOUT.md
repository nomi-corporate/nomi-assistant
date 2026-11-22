# Interpretação de compromissos — ativação no ambiente de desenvolvimento

Rota `POST /api/v1/commitment-proposals` (TEC-003 §7, subconjunto do MVP). O texto do estudante é interpretado por Claude Haiku 4.5 no Amazon Bedrock (Converse API com ferramenta obrigatória). A saída da IA é validada pelo `CommitmentProposalBuilder` antes de virar proposta. A rota nunca cria compromissos: o Mobile envia a proposta revisada ao Academic, que revalida os dados.

## Comportamento

- **Fora do tema acadêmico:** status `unsupported`, sem candidato, com mensagem para o usuário. Exemplos: compras, saúde, lazer ou pedidos ao assistente.
- **Matéria e professor:** são lidos no Academic (`/api/v1/subjects` e `/api/v1/professors`) com o token do próprio usuário. A proposta só usa IDs que pertencem a ele. Quando a fala cita um professor que leciona uma única matéria, essa matéria é vinculada automaticamente. Se ele leciona várias, a proposta pergunta qual.
- **Data ausente, passada ou inválida:** status `needsClarification`, com pergunta. Um horário que não existe no fuso (horário de verão) também gera pergunta.
- **Limite de uso:** 30 interpretações a cada 10 minutos por usuário. Acima disso, responde 429. O limite fica em memória e vale por instância.
- **Indisponibilidade:** quando o Bedrock está indisponível, responde 503 e o Mobile abre o formulário manual. O texto não é registrado em log.
- **Fora do MVP:** as propostas não são persistidas. `messages` (esclarecimento) e `DELETE` ficam para depois; o usuário corrige no formulário. O `Idempotency-Key` é aceito, mas não é armazenado.

## Configuração

| Variável | Valor de desenvolvimento |
| --- | --- |
| `Academic__BaseUrl` | `https://nomi-dev-equipe.duckdns.org/academic` |
| `Interpretation__ModelId` | `us.anthropic.claude-haiku-4-5-20251001-v1:0` (padrão do código) |
| `Interpretation__Enabled` | omitido; `false` desliga a rota (503) |
| `AWS__Region` | `us-east-2` |

Os valores ficam no `environment` do `compose.yaml` do Assistant (repositório nomi-academic, `infra/aws/dev`) e não são segredos.

## Passos na conta AWS

1. **Formulário de caso de uso da Anthropic** (uma vez por conta): Bedrock → Model catalog → Claude Haiku 4.5 → enviar os detalhes de caso de uso. Sem isso, o Bedrock responde `ResourceNotFoundException: Model use case details have not been submitted`. Depois do envio, a liberação pode levar até 15 minutos.
2. **Permissão da role da EC2:** pilha `nomi-dev-interpretation` com `infra/aws/interpretation-bedrock.yaml`. Ela concede apenas `bedrock:InvokeModel` no perfil de inferência dos EUA e no modelo base das três regiões do perfil.
   ```bash
   aws cloudformation deploy --stack-name nomi-dev-interpretation \
     --template-file infra/aws/interpretation-bedrock.yaml \
     --parameter-overrides AssistantRoleName=nomi-dev-ec2-role --capabilities CAPABILITY_IAM
   ```
3. Publicar a imagem pelo CI, atualizar o digest no `release.env` e rodar `compose.py up assistant`.

## Teste com o modelo real

Os testes `BedrockInterpreterLiveTests` só chamam o modelo com `NOMI_BEDROCK_LIVE=1` e credenciais autorizadas:

```bash
NOMI_BEDROCK_LIVE=1 AWS_PROFILE=nomi-dev AWS_REGION=us-east-2 dotnet test tests/Nomi.Assistant.Tests --filter "FullyQualifiedName~Live"
```

Custo de referência: cada interpretação usa cerca de 1.000 tokens de entrada e até 800 de saída no Haiku 4.5. Confira a tabela de preços do Bedrock antes de ampliar o uso.
