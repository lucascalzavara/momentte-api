# Planos, eventos e Mercado Pago

## Preparação

Banco existente: execute manualmente `scripts/003_plans_events.sql`. Banco novo: `scripts/init.sql` já contém o schema. Não há migrations nem execução automática de SQL.

Configure via user-secrets (ambiente local) ou variáveis de ambiente (produção), sem versionar os valores:

| Chave | Valor |
| --- | --- |
| `MercadoPago:AccessToken` | Token da aplicação/conta vendedora |
| `MercadoPago:WebhookSecret` | Segredo de validação das notificações |
| `MercadoPago:CollectorId` | ID da conta recebedora; quando informado, conferido em cada pagamento |
| `MercadoPago:PublicApiUrl` | URL pública HTTPS desta API |
| `MercadoPago:WebUrl` | URL pública HTTPS da Momentte.Web |
| `MercadoPago:Sandbox` | `true` para usar a URL de checkout de teste de compras avulsas |
| `Media:RootPath` | Pasta privada persistente; padrão `Momentte.Api/App_Data/media` |

Use credenciais de teste para homologação. O flag Sandbox não transforma credenciais produtivas em credenciais de teste. Não há chamadas externas nos testes incluídos. Configure as notificações `payment`, `subscription_authorized_payment` e `subscription_preapproval` para `/api/v1/billing/mercadopago/webhook`. Notificações sem assinatura válida não liberam benefícios. Há conciliação manual e periódica para recuperar pagamentos que ainda não foram notificados.

Os fluxos seguem as referências oficiais: [preferências Checkout Pro](https://www.mercadopago.com.br/developers/pt/reference/online-payments/checkout-pro-preferences/overview), [assinaturas pendentes](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/integration-configuration/subscription-no-associated-plan/pending-payments), [gerenciamento de assinaturas](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/subscription-management) e [Webhooks](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/additional-content/your-integrations/notifications/webhooks).

## Regras

- Gratuito: marca Momentte, 1 GB decimal por evento, download durante o evento e até 24h depois do término.
- Evento pago: customização de cor/logo, 10 GB decimal e até 72h de download após o término. Os benefícios são gravados no evento e independem de compra ou assinatura; cancelamento/expiração da assinatura não os altera.
- Avulso: R$30. Assinatura: R$99, 8 ativações por ciclo pago, sem acúmulo. Excedente: R$20 somente quando existe ciclo vigente com as 8 ativações já usadas.
- Organização não consome franquia/compra. Ativar com `subscription` consome uma vaga; com `purchase`, consome uma compra aprovada; com `free`, inicia sem benefícios pagos e sem consumo. Repetir a ativação é idempotente.
- Uma compra em organização é vinculada ao evento. Excluir o rascunho ou escolher outra origem ao iniciar preserva a compra não consumida como crédito para outro evento. Não há devolução automática de ativações consumidas.
- Nome, término, tema e logo ficam travados após iniciar. Encerrar antecipadamente é permitido e inicia imediatamente a janela de download. Não há extensão de prazo após ativar.
- Estados calculados pelo relógio UTC do servidor: `organization`, `active`, `awaiting_download`, `finalized`. O acesso às mídias termina exatamente no prazo, mesmo se o processo de limpeza não estiver executando.
- A exclusão física ocorre em segundo plano, a cada 5 minutos, com nova tentativa após falhas. O organizador só pode excluir eventos em organização; pagamentos pendentes bloqueiam a exclusão para evitar uma compra sem destino.
- Imagens JPEG, PNG e WebP; até 50 MB por foto e 5 MB por logo. Quota mede bytes realmente recebidos, inclui a logo e é aplicada sob lock da conta. O armazenamento é privado, sem URLs públicas permanentes. `IMediaStore` isola a implementação local para posterior bucket externo.
- Upload, download e telão atuais são do organizador autenticado. Acesso de convidados por convite/QR e infraestrutura cloud não fazem parte desta etapa.

## API

Todas as rotas abaixo exigem JWT e usam o usuário de `sub`; IDs de outra conta não dão acesso.

| Método/rota | Uso |
| --- | --- |
| GET `/api/v1/billing` | Plano, ciclo, consumo e compras |
| POST `/api/v1/billing/checkout` | `{kind:"subscription",planCode:"professional",requestKey:UUID}` ou `{kind:"event",eventId:UUID,requestKey:UUID}` |
| POST `/api/v1/billing/change-plan` | `{planCode:"free"}` cancela renovação e preserva o período pago |
| POST `/api/v1/billing/reconcile` | Consulta o Mercado Pago e atualiza pagamentos pendentes |
| GET/POST `/api/v1/events` | Listar/criar; criação recebe `name`, `endsAt` com offset e `themeColor` opcional |
| PUT `/api/v1/events/{id}` | Editar organização |
| DELETE `/api/v1/events/{id}` | Excluir organização |
| DELETE `/api/v1/events/{id}/customization` | Remover logo/cor antes de iniciar |
| POST `/api/v1/events/{id}/activate` | `{source:"free" | "subscription" | "purchase"}` |
| POST `/api/v1/events/{id}/end` | Encerrar antecipadamente |
| POST `/api/v1/events/{id}/media` ou `/logo` | Multipart, campo `file` |
| GET `/api/v1/events/{id}/media` | Listar fotos disponíveis |
| GET `/api/v1/events/{id}/media/{mediaId}` ou `/logo` | Arquivo, com Bearer e prazo verificado |

## Pagamento e validação

Retorno do navegador não é prova de pagamento. O servidor consulta o recurso do Mercado Pago, verifica moeda e valor e registra o ID do pagamento uma única vez. Assinatura apenas autorizada não libera franquia: é necessário pagamento aprovado de uma fatura. Cada ciclo tem contador próprio, sem zerar o histórico anterior. Os locks de conta serializam consumo e alterações concorrentes.

Compras duplicadas e upgrades tardios são classificados como `review_required` em vez de gerar benefícios extras. Estornos/contestações são registrados quando notificados; reembolso financeiro e tratamento administrativo não são automatizados nesta etapa. Uma tentativa de checkout usa UUID estável e recupera recursos existentes por referência externa antes de criar novamente.

Verificações: `dotnet run --project tests/Momentte.Api.Checks`. Cobrem preços, limites, prorrata, estados/prazos, assinatura HMAC, payloads do Mercado Pago simulados, formato/bytes de upload e limpeza de arquivo parcial. Não cobrem a execução SQL e a transação real no PostgreSQL nem uma venda real. Homologue com banco dedicado, credenciais de teste e webhook público antes de produção.
