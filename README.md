# Nomi Assistant

Interpretação de texto e voz e integração com provedores de IA. Retorna propostas estruturadas para confirmação; não altera o banco Academic diretamente.

## Stack acordada

C# e ASP.NET Core.

Infraestrutura AWS; autenticação Amazon Cognito; arquivos no S3. Cada serviço é implantado independentemente e controla seus próprios dados.

## Estado inicial

Este repositório contém a documentação e os checks iniciais. O código da aplicação, o provisionamento AWS e o deploy ainda não foram implementados.

## Documentação

- [Arquitetura](docs/architecture.md)
- [CI/CD e proteção da master](docs/delivery.md)
- [Como contribuir](CONTRIBUTING.md)
