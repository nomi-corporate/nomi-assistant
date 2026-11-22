# Nomi Assistant

Interpretação de texto e voz e integração com provedores de IA. Retorna propostas estruturadas para confirmação; não altera o banco Academic diretamente.

## Stack acordada

C# e ASP.NET Core.

Infraestrutura AWS; sessão validada pelo Nomi Identity; arquivos privados no S3. Cada serviço é implantado independentemente e controla seus próprios dados.


## Executar localmente

Instale o SDK .NET 10 compatível com `global.json`.

```sh
dotnet restore Nomi.Assistant.sln
dotnet build Nomi.Assistant.sln
dotnet run --project src/Nomi.Assistant.Api
```

Liveness: `http://localhost:5102/health/live` confirma somente que o processo responde. Readiness em `/health/ready` retorna 200 quando o PostgreSQL está acessível e 503 sem configuração ou quando a conexão falha. Consulte [PostgreSQL e execução na AWS](docs/postgresql.md).

## Estrutura

- `src/Nomi.Assistant.Api`: HTTP e composição das dependências.
- `src/Nomi.Assistant.Application`: casos de uso e portas de integração.
- `src/Nomi.Assistant.Domain`: regras e modelos de domínio.
- `src/Nomi.Assistant.Infrastructure`: adapters de persistência e provedores externos.

A transcrição de áudio usa Amazon Transcribe em lote. O Mobile grava M4A, solicita uma URL temporária para upload privado no S3, inicia a transcrição e consulta seu resultado. O Assistant valida a sessão pelo Nomi Identity, confere tamanho, SHA-256 e duração do arquivo antes de iniciar o job, e guarda apenas o texto até a expiração. A transcrição ainda não gera proposta estruturada de matéria ou compromisso; essa interpretação é uma etapa separada. Sem conexão PostgreSQL, a API inicia com liveness saudável e readiness 503. Nenhuma migration é aplicada no startup.

### Configuração da transcrição

Defina `ConnectionStrings__Assistant`, `Identity__BaseUrl`, `Transcription__BucketName` e `AWS__Region` no ambiente do Assistant. As credenciais AWS vêm da cadeia padrão do SDK, preferencialmente uma role de execução; não as coloque no Mobile ou no repositório. A região padrão no código é `us-east-2`. O `appsettings.json` genérico não fixa um bucket; o Compose de desenvolvimento fornece o bucket criado na AWS. Sem bucket configurado e permissões, as rotas retornam 503.

O bucket precisa permitir à role do Assistant `s3:GetObject`, `s3:PutObject`, `s3:DeleteObject` apenas nos prefixos `audio/` e `transcripts/`, além de `transcribe:StartTranscriptionJob`, `transcribe:GetTranscriptionJob` e `transcribe:DeleteTranscriptionJob`. O serviço Amazon Transcribe também precisa de permissão para ler o áudio e escrever o resultado nesse bucket. Mantenha bloqueio de acesso público e uma regra de ciclo de vida para apagar `audio/` e `transcripts/` após no máximo dois dias; o worker do Assistant remove registros expirados e tenta apagar os objetos a cada hora, mas a regra S3 cobre interrupções e jobs ainda em processamento.

O template [transcription-bucket.yaml](infra/aws/transcription-bucket.yaml) prepara o bucket privado e a política para uma role de execução existente, caso seu nome seja informado. Em 21/11/2026, o responsável mostrou a stack `nomi-dev-transcription` em `CREATE_COMPLETE` na região `us-east-2`, informou o bucket `nomi-dev-transcription-transcriptionbucket-6rcao9odq3hg` e confirmou `AssistantRoleName=nomi-dev-ec2-role`. O Compose de desenvolvimento referencia esse bucket. Ainda faltam conferir as permissões efetivas, aplicar a migration, publicar o Assistant atualizado e validar uma transcrição real; a stack criada não comprova essas etapas. A criação do bucket e jobs de transcrição podem gerar cobrança.

Rotas: `POST /api/v1/audio-uploads` recebe `contentType: audio/mp4`, `sizeBytes`, `durationSeconds`, `sha256` hexadecimal e `locale: pt-BR`, e retorna `audioId` e URL PUT válida por dez minutos. `POST /api/v1/transcriptions` exige `Idempotency-Key` e devolve 202; `GET /api/v1/transcriptions/{id}` devolve o estado e texto; `DELETE` cancela o acesso e solicita remoção dos artefatos. O limite é 10 MB e 60 segundos. O texto expira após 24 horas. A migration `AddAudioTranscriptions` deve ser aplicada antes de usar as rotas.

## Contêiner

```sh
docker build -t nomi-assistant:dev .
docker run --rm -p 5102:8080 nomi-assistant:dev
```

CI verifica compilação, publicação, liveness e construção da imagem. Após os checks da master, o workflow publica uma imagem validada no ECR usando OIDC e registra seu digest. Configure as três variáveis descritas em [Entrega](docs/delivery.md). A implantação na EC2 ainda não está implementada.

## Documentação

- [PostgreSQL e execução na AWS](docs/postgresql.md)
- [Arquitetura](docs/architecture.md)
- [Entrega](docs/delivery.md)
- [Contribuição](CONTRIBUTING.md)
