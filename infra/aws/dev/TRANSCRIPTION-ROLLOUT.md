# Ativação da transcrição de áudio (dev, us-east-2)

Já feito: stack `nomi-dev-transcription` (`CREATE_COMPLETE`), bucket
`nomi-dev-transcription-transcriptionbucket-6rcao9odq3hg`, política `nomi-assistant-transcription`
na role `nomi-dev-ec2-role`, Compose com `Transcription__BucketName`/`AWS__Region`, rota Caddy
`/assistant/api/v1/*` e Mobile aceitando `EXPO_PUBLIC_ASSISTANT_API_URL=https://<domínio>/assistant`.

Em 22/11/2026, conferido na conta: hop limit do IMDS já era 2 (passo 2 ok), `Identity__BaseUrl` foi
adicionado ao `assistant.env` (passo 3 ok) e a rota `/assistant/api/v1/*` foi aplicada no Caddy da EC2
(backup `Caddyfile.bak-<data>`). SQL e script da migration já estão em `~/nomi/migrations`.

Roteiro completo (todos exigem acesso à conta AWS/EC2):

1. **Publicar a imagem**: commit + PR de `feat/aws-transcription` no `nomi-assistant`; após merge na
   master o CI publica no ECR. Copiar o `image_digest_uri` para `NOMI_ASSISTANT_IMAGE` em
   `nomi-academic/infra/aws/dev/release.env`.
2. **Credenciais da role dentro do contêiner**: o SDK usa o IMDS da EC2. Contêiner em rede bridge
   precisa de hop limit ≥ 2:
   `aws ec2 modify-instance-metadata-options --instance-id <id> --http-put-response-hop-limit 2 --http-tokens required`
   (conferir antes com `aws ec2 describe-instances --instance-ids <id> --query 'Reservations[].Instances[].MetadataOptions'`).
3. **`~/nomi/env/assistant.env`** na EC2: além de `ConnectionStrings__Assistant`, conter
   `Identity__BaseUrl=https://nomi-dev-equipe.duckdns.org`. Sem isso as rotas devolvem 503.
4. **Migration** `AddAudioTranscriptions` no RDS: copiar o SQL revisado
   (`artifacts/add-audio-transcriptions.sql`, SHA-256 `e46c6165…bf18a`, o mesmo esperado pelo script) para
   `~/nomi/migrations/add-audio-transcriptions.sql` e rodar `apply-audio-migration.sh --check`, depois
   sem argumento (pede a senha do `nomi_assistant_migrator`). O usuário de aplicação precisa de
   `SELECT/INSERT/UPDATE/DELETE` nas tabelas novas do schema `assistant`.
5. **Implantar**: copiar `Caddyfile`, `compose.yaml` e `release.env` atualizados para `~/nomi/deploy`,
   `python3 compose.py check`, `pull assistant`, `up assistant` e recarregar o Caddy.
6. **Validar**: `curl https://<domínio>/assistant/health/ready` = 200; no app, gravar 5–10 s e
   conferir texto no formulário. Jobs do Transcribe geram cobrança por segundo de áudio.
