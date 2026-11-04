# Nomi Assistant

Interpretação de texto e voz e integração com provedores de IA. Retorna propostas estruturadas para confirmação; não altera o banco Academic diretamente.

## Stack acordada

C# e ASP.NET Core.

Infraestrutura AWS; autenticação Amazon Cognito; arquivos no S3. Cada serviço é implantado independentemente e controla seus próprios dados.


## Executar localmente

Instale o SDK .NET 10 compatível com `global.json`.

```sh
dotnet restore Nomi.Assistant.sln
dotnet build Nomi.Assistant.sln
dotnet run --project src/Nomi.Assistant.Api
```

Liveness: `http://localhost:5102/health/live`. O endpoint confirma somente que o processo responde; conexões de banco e filas ainda serão implementadas.

## Estrutura

- `src/Nomi.Assistant.Api`: HTTP e composição das dependências.
- `src/Nomi.Assistant.Application`: casos de uso e portas de integração.
- `src/Nomi.Assistant.Domain`: regras e modelos de domínio.
- `src/Nomi.Assistant.Infrastructure`: adapters de persistência e provedores externos.

Ainda não há endpoints acadêmicos, autenticação Cognito, banco configurado ou envio de notificações. A base executa sem credenciais AWS. As próximas entregas adicionam as funcionalidades da TEC-003.

## Contêiner

```sh
docker build -t nomi-assistant:dev .
docker run --rm -p 5102:8080 nomi-assistant:dev
```

CI verifica compilação, publicação, liveness e construção da imagem. CD depende da infraestrutura e das permissões AWS.

## Documentação

- [Arquitetura](docs/architecture.md)
- [Entrega](docs/delivery.md)
- [Contribuição](CONTRIBUTING.md)
