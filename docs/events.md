# Organização e participação em eventos

Em banco existente, aplique manualmente os scripts pendentes em ordem, até `scripts/004_event_participation.sql`. Bancos novos usam `scripts/init.sql`. A aplicação não executa SQL nem migrations.

## Configuração

POST `/api/v1/events` e PUT `/api/v1/events/{id}` recebem `name` (100 caracteres), `startsAt`, `endsAt` (datas com offset), `mediaMode` (`images`, `texts`, `both`), `moderationEnabled`, `themeColor`, `textColor`, `fontFamily` (`sans`, `serif`, `rounded`) e `textScale` (`small`, `medium`, `large`). O término deve ser futuro e a duração positiva, de até 48 horas. A logo é enviada separadamente, pelo endpoint existente, até 5 MB; DELETE `/api/v1/events/{id}/logo` remove apenas a logo.

O rascunho admite todas as opções de aparência. Ativar exige início previsto atingido e término ainda futuro. No gratuito, confirme `acceptDefaultAppearance: true`; aplica-se a aparência Momentte sem apagar a configuração salva. Qualquer ativação trava todas as configurações. Finalizar manualmente ou atingir o término interrompe os envios e inicia a janela de download. O prazo nunca é prorrogado.

Compras avulsas pertencem à conta até serem consumidas ao iniciar. Checkout usa `{kind:"event",requestKey:UUID}`, sem eventId. Iniciar com `purchase` consome um crédito aprovado, `subscription` consome uma ativação do ciclo vigente e `free` não consome créditos. Não há devolução automática após ativação.

## Convites e conteúdo

O link estável `/e/{publicCode}` e o QR são gerados ao salvar. QR estilizado tem exportação SVG/PNG. GET `/api/v1/participation/{code}` fornece informações públicas mínimas; as demais rotas exigem JWT:

| Rota | Função |
| --- | --- |
| POST `/api/v1/participation/{code}/join` | Vincula convidado ao evento, sem consumir crédito |
| GET `/api/v1/participation` | Eventos dos quais participa |
| GET/POST `/api/v1/participation/{code}/submissions` | Próprios envios / envio multipart (`file`, `text`, `showName`) |
| GET `/api/v1/participation/{code}/submissions/{mediaId}/preview` | Prévia da própria imagem |
| GET `/api/v1/participation/{code}/logo` | Logo paga para participante autenticado |
| GET `/api/v1/events/{id}/submissions` | Gestão dos envios pelo organizador |
| GET `/api/v1/events/{id}/feed` | Conteúdos aprovados do evento ativo |
| GET `/api/v1/events/{id}/submissions/{mediaId}/preview` | Prévia para moderação |
| PUT `/api/v1/events/{id}/submissions/{mediaId}/moderation` | `{status:"approved"\|"rejected"\|"pending"}` |
| DELETE `/api/v1/events/{id}/submissions/{mediaId}` | Remove envio |
| GET `/api/v1/events/{id}/texts` | CSV dos textos/legendas aprovados após encerrar |

Imagens: JPEG/PNG/WebP, até 20 MB. Texto/legenda: até 500 caracteres. Em `both`, aceite imagem, texto ou ambos; envio vazio é rejeitado. Cada envio é associado à conta; exibir o nome no telão é opcional. Moderação funciona nos dois níveis: habilitada gera pendência; desabilitada aprova automaticamente. Apenas aprovados aparecem no telão e no acervo para download. Não foi adicionada validação de idade.

“Organizo” e “Participo” separam os papéis. Convidados não acessam gestão, outros envios ou downloads do acervo. O login/cadastro preserva o convite. O telão `/telao/{id}` exige a sessão do organizador e consulta o feed a cada 5 segundos; novas abas usam sessões independentes e podem exigir login.

Gratuito: 1 GB e 24h de download após encerrar. Pago: 10 GB e 72h, independentemente da origem. Expiração bloqueia acesso imediatamente; exclusão física acontece no serviço de retenção. O armazenamento continua privado e local, preparado para substituição por bucket.

## Validação

Builds da API e Web; verificações em memória da autenticação e das regras de eventos; testes de leitura dos QR em três tamanhos. A fixture local de navegador simula a API e não realiza cobranças. Estes testes não substituem homologação de SQL, permissões e concorrência em PostgreSQL nem de pagamentos reais.
