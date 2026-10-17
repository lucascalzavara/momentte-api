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

### Download de todas as imagens

Após encerrar e dentro da janela de download, o organizador pode baixar todas as imagens aprovadas em ZIP. O acervo completo é consultado, sem o limite de 200 registros da tela. Textos, logos, mídias removidas, pendentes e rejeitadas não entram no arquivo; textos e legendas continuam disponíveis pelo CSV.

POST `/api/v1/events/{id}/images/archive-ticket` exige JWT e valida acesso/prazo/imagens. Retorna `ticket` de uso único, válido por dois minutos. GET `/api/v1/events/{id}/images/archive?ticket=...` consome essa autorização e revalida acesso/prazo antes de iniciar. O JWT não é colocado na URL. Há um ticket vigente por organizador e limite de duas solicitações por minuto. Sem imagens aprovadas, retorna 404.

O ZIP é enviado por streaming ao gerenciador de downloads do navegador, com extensões JPEG/PNG/WebP e nomes únicos. Não há arquivo temporário nem carregamento do ZIP completo em memória. Uma transferência interrompida deve ser solicitada novamente; não há retomada por intervalos. Em grandes acervos, o tempo depende da rede e do armazenamento. Os testes não simulam 10 GB nem carga concorrente de produção.

Os tickets ficam na memória da instância e são invalidados ao reiniciar. Uma implantação com múltiplas réplicas precisa manter emissão e consumo na mesma instância ou substituir esse armazenamento por um serviço compartilhado com consumo atômico. A configuração atual continua voltada à instância única e armazenamento local.

### Atualizações durante o evento

O polling permanece nesta etapa. A evolução recomendada é usar SignalR apenas para notificações pequenas, agrupadas por evento e com autorização, mantendo fotos por HTTP. Para picos, agrupar avisos e sincronizar o estado após reconexão. Múltiplas instâncias exigem distribuição dos avisos entre servidores. Não foi introduzido WebSocket nesta alteração.

Builds da API e Web; verificações em memória da autenticação e das regras de eventos; testes de leitura dos QR em três tamanhos. A fixture local de navegador simula a API e não realiza cobranças. Estes testes não substituem homologação de SQL, permissões e concorrência em PostgreSQL nem de pagamentos reais.

## QR opcional e controles do telão

Banco existente: aplique manualmente `scripts/005_event_screen_qr.sql`. Novos bancos usam apenas o `init.sql` atualizado. A configuração `showQrCode` é booleana, inicia habilitada e pode ser editada somente em organização, nos dois planos. Desativar oculta o QR do telão sem alterar o convite ou o QR disponível na gestão. A prévia acompanha a configuração.

No telão, o QR é somente visual, sem botões de copiar/baixar. Sem QR, o conteúdo usa a largura disponível. Os controles de gestão/tela cheia começam ocultos, aparecem com mouse, toque ou teclado e desaparecem após três segundos sem interação. Enquanto houver foco de teclado nos controles, eles permanecem visíveis por acessibilidade.
