# Momentte.Api

Planos, ciclo de vida dos eventos, armazenamento privado e Mercado Pago: consulte [docs/plans-and-payments.md](docs/plans-and-payments.md). Banco existente: execute `scripts/003_plans_events.sql` além dos scripts anteriores.

API em .NET 10, seguindo a organização do Alicerce.Api: Controllers, Application (Interfaces/Implementations), Domain (DTOs/Entities/Exceptions) e Infrastructure (Interfaces/Implementations). PostgreSQL + Dapper, BCrypt e JWT. Sem Entity Framework ou migrations.

## Banco de dados

Crie um banco PostgreSQL 13+ dedicado chamado `momentte` e execute manualmente [scripts/init.sql](scripts/init.sql). Exemplo com psql:

```sh
psql -h localhost -U postgres -d momentte -v ON_ERROR_STOP=1 -f scripts/init.sql
```

O script não é executado pela aplicação e não contém usuários ou senhas padrão. Pode ser repetido em um banco com o mesmo schema; não atualiza estruturas preexistentes. O índice único de e-mail também impede duplicatas em cadastros simultâneos.

Se o banco já foi inicializado anteriormente, execute apenas `scripts/002_refresh_tokens.sql` antes de atualizar a aplicação. Novos bancos já recebem a tabela de refresh tokens pelo `init.sql`. Sem essa tabela, cadastro, login e refresh não podem persistir as sessões.

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
  "refreshToken":"TOKEN_ALEATORIO",
  "refreshExpiresAt":"2026-10-27T21:00:00Z",
  "tokenType":"Bearer",
  "expiresIn":3600,
  "expiresAt":"2026-10-12T22:00:00Z",
  "user":{"id":"00000000-0000-0000-0000-000000000000","name":"Ana Silva","email":"ana@example.com"}
}
```

E-mail inexistente e senha incorreta retornam **401** com a mesma mensagem. Dados inválidos retornam **400**. Erros seguem ProblemDetails, sem detalhes internos. Respostas nunca incluem hash ou senha. JWT contém `sub` (ID), `jti`, `email` e `name`. Use `Authorization: Bearer TOKEN` nos futuros endpoints protegidos.

Os endpoints de autenticação têm limite compartilhado de 10 tentativas por minuto por IP, com **429** e `Retry-After`. O limitador é local a cada instância; ao implantar atrás de proxy, configure encaminhamento apenas de proxies confiáveis para preservar o IP correto.

### POST /api/v1/auth/refresh-token

```json
{"refreshToken":"TOKEN_RECEBIDO_NO_LOGIN_OU_ULTIMO_REFRESH"}
```

Retorna **200** no mesmo formato de login, incluindo novos access/refresh tokens. Não exige JWT válido: a credencial é o refresh token. Token desconhecido, expirado, revogado ou já consumido retorna **401**. Access token dura 60 minutos e refresh token 15 dias por padrão (`Jwt:RefreshTokenExpirationDays`, de 1 a 365). A rotação reinicia a validade de 15 dias; cada login cria uma sessão independente.

O token é gerado com 64 bytes criptograficamente aleatórios; apenas seu hash SHA-256 é armazenado no banco. A transação revoga o token anterior e insere o substituto, com UPDATE condicional para que só uma requisição possa consumir cada token. Cadastro persiste usuário e refresh token juntos, também em transação. O cliente deve substituir ambos os tokens a cada resposta e compartilhar uma chamada de refresh entre requisições simultâneas.

### POST /api/v1/auth/revoke-token

Recebe o mesmo JSON do refresh e retorna **204**, inclusive para tokens desconhecidos ou já revogados. Revoga apenas a sessão informada. O access JWT emitido continua válido até sua expiração; sair no frontend descarta ambos os tokens. Quando a revogação não pode chegar ao servidor (por exemplo, offline), somente o logout local é garantido.

A Momentte.Web integra cadastro, login, renovação automática e logout. Não há empresas, eventos, confirmação de e-mail ou recuperação de senha nesta etapa.

## Verificação

```sh
dotnet build Momentte.Api.slnx
dotnet run --project tests/Momentte.Api.Checks
```

As verificações usam repositórios em memória e validam cadastro/login, BCrypt, JWT, entrada, rotação, rejeição de tokens consumidos/expirados, hashes e revogação. Não substituem a verificação da transação e concorrência no PostgreSQL após executar o SQL.
