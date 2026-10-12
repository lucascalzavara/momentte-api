# Momentte.Api

API em .NET 10, seguindo a organização do Alicerce.Api: Controllers, Application (Interfaces/Implementations), Domain (DTOs/Entities/Exceptions) e Infrastructure (Interfaces/Implementations). PostgreSQL + Dapper, BCrypt e JWT. Sem Entity Framework ou migrations.

## Banco de dados

Crie um banco PostgreSQL 13+ dedicado chamado `momentte` e execute manualmente [scripts/init.sql](scripts/init.sql). Exemplo com psql:

```sh
psql -h localhost -U postgres -d momentte -v ON_ERROR_STOP=1 -f scripts/init.sql
```

O script não é executado pela aplicação e não contém usuários ou senhas padrão. Pode ser repetido em um banco com o mesmo schema; não atualiza estruturas preexistentes. O índice único de e-mail também impede duplicatas em cadastros simultâneos.

## Configuração e execução

Configure os segredos localmente, substituindo os exemplos abaixo. Não versione credenciais:

```sh
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=momentte;Username=SEU_USUARIO;Password=SUA_SENHA" --project Momentte.Api
dotnet user-secrets set "Jwt:SecretKey" "SUA_CHAVE_ALEATORIA_COM_PELO_MENOS_32_BYTES" --project Momentte.Api
dotnet dev-certs https --trust
dotnet run --project Momentte.Api --launch-profile https
```

Swagger: https://localhost:7051/swagger. Em produção use `ConnectionStrings__DefaultConnection` e `Jwt__SecretKey` no ambiente/gestor de segredos. A API recusa iniciar sem uma chave JWT configurada. `Jwt:Issuer`, `Jwt:Audience` e expiração (60 minutos por padrão) estão no appsettings. CORS de desenvolvimento permite a Momentte.Web na porta 5174; configure `Cors:AllowedOrigins` em produção.

## Endpoints

### POST /api/v1/auth/register

```json
{"name":"Ana Silva","email":"ana@example.com","password":"UmaSenhaForte123!"}
```

Retorna **201** com token e usuário. Nome obrigatório, até 150 caracteres; e-mail válido, até 254; senha com pelo menos 8 caracteres e no máximo 72 bytes UTF-8 (limite do BCrypt). Senhas não são aparadas ou normalizadas. Nome é aparado; e-mail é aparado e convertido para minúsculas. E-mail já cadastrado retorna **409**.

### POST /api/v1/auth/login

```json
{"email":"ana@example.com","password":"UmaSenhaForte123!"}
```

Retorna **200** no mesmo formato do cadastro:

```json
{
  "accessToken":"JWT",
  "tokenType":"Bearer",
  "expiresIn":3600,
  "expiresAt":"2026-10-12T22:00:00Z",
  "user":{"id":"00000000-0000-0000-0000-000000000000","name":"Ana Silva","email":"ana@example.com"}
}
```

E-mail inexistente e senha incorreta retornam **401** com a mesma mensagem. Dados inválidos retornam **400**. Erros seguem ProblemDetails, sem detalhes internos. Respostas nunca incluem hash ou senha. JWT contém `sub` (ID), `jti`, `email` e `name`. Use `Authorization: Bearer TOKEN` nos futuros endpoints protegidos.

Cadastro/login têm limite compartilhado de 10 tentativas por minuto por IP, com **429** e `Retry-After`. O limitador é local a cada instância; ao implantar atrás de proxy, configure encaminhamento apenas de proxies confiáveis para preservar o IP correto.

Esta etapa inclui apenas cadastro e login. Não há empresas, eventos, refresh tokens, confirmação de e-mail ou recuperação de senha. Ao expirar o token, faça login novamente. A Momentte.Web ainda não está integrada.

## Verificação

```sh
dotnet build Momentte.Api.slnx
dotnet run --project tests/Momentte.Api.Checks
```

As verificações usam um repositório em memória e validam normalização, hash BCrypt, duplicidade, login inválido, assinatura/claims do JWT e validação de entrada. Não substituem a verificação de integração após executar o SQL no seu PostgreSQL.
