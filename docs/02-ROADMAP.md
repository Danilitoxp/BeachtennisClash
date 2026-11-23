# Beach Tennis 3D Mobile — Roadmap por Etapas

Cada fase só começa quando os **critérios de validação** da anterior forem cumpridos — de preferência em aparelho real, não só no editor.

Legenda de esforço (1 dev sênior em tempo integral, sem arte): S = 1–2 semanas · M = 2–4 semanas · L = 4–8 semanas.

---

## Fase 0 — Fundação técnica (S)
**Objetivo:** projeto configurado, build rodando em Android e iOS, medição de desempenho funcionando.

- Projeto Unity 6 LTS com URP (assets de renderer por tier: Baixo/Médio/Alto/Ultra).
- Pacotes: Input System, Cinemachine 3, Addressables, Animation Rigging, Test Framework, Adaptive Performance, Mathematics, Burst.
- Estrutura de pastas e asmdefs (ver doc 01 §3.2–3.5), `.gitignore`, Git LFS.
- Player Settings: IL2CPP ARM64, Vulkan/Metal, ASTC, frame pacing, target 60/120.
- Cena `Boot` + composition root + tick fixo (120 Hz) com interpolação.
- Painel de debug in-game (FPS, frame time CPU/GPU, memória, GC) e cena de benchmark.
- Cena gray-box da quadra em escala real (16 × 8 m, rede).

**Validação:**
- [ ] Build instalado em 1 Android do tier mínimo, 60 FPS estáveis por 20 min na cena gray-box. *(iOS adiado: sem Mac. Rodando no BlueStacks a 60 FPS, mas emulador não vale como medição.)*
- [x] Painel de debug exibindo métricas no APK (BlueStacks, 22/11/2026). Profiler conectado a aparelho real: pendente.
- [x] 0 B de GC por frame na cena de partida vazia (BlueStacks, 22/11/2026: 0/29 frames).
- [x] 12 testes EditMode passando (relógio de simulação, scheduler, quadra).

## Fase 1 — Física da bola (M)
**Objetivo:** bola com trajetória convincente e previsível.

- `BallSimulation` (gravidade, arrasto, Magnus, decaimento de spin), colisões analíticas com rede, areia, limites.
- Previsão de trajetória (mesma função da simulação).
- `ShotSolver`: intenção → velocidade/spin, com erro por timing.
- Ferramenta de editor: visualizador de trajetória e painel de tuning ao vivo.
- Testes EditMode: alcance, altura sobre a rede, determinismo (mesma entrada → mesmo resultado), previsão = simulação.

**Validação:**
- [x] Testes passando (35 EditMode, 22/11/2026: física, rede, areia, determinismo, solver).
- [ ] Lob, smash, voleio rápido e bola curta "parecem certos" visualmente em aparelho (revisão sua). *Sandbox "máquina de bolas" pronto no BlueStacks, com câmera de fundo e lateral.*
- [x] Simulação da bola < 0,1 ms/frame: ~0,6 µs por tick no editor (2 ticks por frame). Solver: 0,09–0,28 ms por rebatida no BlueStacks. *Confirmar em aparelho real.*

**Decisões tomadas nesta fase (revisáveis):**
- Golpes cuja trajetória não passa a rede com 10 cm de folga têm o arco elevado automaticamente (`EnforceNetClearance`). Um voleio baixo vira um golpe arqueado mais lento, como na vida real. A Fase 2 decide quando o jogador pode forçar um golpe reto (risco de rede).
- A bola é desenhada 2,4× maior que o diâmetro real (só no visual), em amarelo fluorescente com rastro curto, sombra de contato forte e câmera mais próxima, para ficar legível em tela de celular.
- **Bug corrigido em 22/11/2026 (pós-Fase 4):** a bola **nunca era desenhada**. O `BallView` desativava o próprio GameObject quando a bola estava inativa e, com isso, parava de rodar e não se reativava mais. Agora ele liga e desliga só os renderizadores. As capturas das Fases 1–4 mostravam só sombra, linha e anéis; eu tinha atribuído a falta da bola ao tamanho, sem conferir.
- Ajuda visual: a linha da trajetória da bola que vem até você fica ligada por padrão na partida e no treino (dá para desligar no menu, opção 6).
- Determinismo garantido na mesma plataforma. Entre ARM e x86 o float pode divergir. Por isso o online (Fase 10) usa servidor autoritativo, não lockstep.

## Fase 2 — Jogador, controles e golpes (gray-box) (M)
**Objetivo:** o núcleo do jogo é divertido com cápsulas.

- `PlayerSimulation` com locomoção na areia (aceleração, frenagem, split-step, mergulho).
- Input mobile: joystick flutuante + golpes por botão/swipe; buffer de input; assistência de posicionamento.
- Janela de contato, qualidade de timing, tipos de golpe (voleio alto/baixo, smash, lob, drop, saque).
- Modo treino contra "máquina de bolas".

**Validação:**
- [x] Latência toque → início do golpe ≤ 50 ms: **18 ms** medidos no BlueStacks (do evento de toque até a simulação; não inclui hardware da tela). *Confirmar em aparelho real.*
- [ ] 5 pessoas testam 10 min: entendem os controles sem tutorial longo e conseguem trocar bola.
- [ ] Você aprova o "feel".
- [x] 50 testes EditMode passando (15 novos: movimento, plano de contato, timing, mergulho, saque, determinismo).

**Como ficou o controle (proposta para validação):**
- Joystick flutuante na metade esquerda: move o jogador.
- Botões GOLPE (contextual: voleio, ou smash acima de 2,25 m), LOB e CURTA.
- **Segurar** = preparar: o jogador corre sozinho até o ponto de contato (anel verde na areia; laranja = só alcança mergulhando) e o joystick passa a **mirar** (anel branco na quadra adversária).
- **Soltar** = bater. Ideal: soltar 0,10 s antes do contato. Janelas: Perfeito ±35 ms, Bom ±85 ms; fora disso Cedo/Tarde, com erro de direção e força. Segurar até o contato = Tarde automático (perdoa o iniciante). Soltar mais de 0,45 s antes = erra a bola.
- Saque: segurar GOLPE joga a bola para cima; soltar bate. A mira define o alvo.
- O tempo do toque usa o timestamp do próprio evento de toque, não o do frame.

**Pontos em aberto para decidir com o teste:**
- A assistência de posicionamento pode estar forte demais (com o botão segurado, sempre chega). Na Fase 4 ela vira parâmetro de dificuldade/acessibilidade.
- O jogador não volta sozinho para a posição base depois de bater.
- A câmera ainda é fixa. Seguir o jogador fica para a Fase 6.

## Fase 3 — Regras e fluxo de partida (S)
- `RulesEngine` data-driven a partir das regras ITF de Beach Tennis (conferidas na íntegra nesta fase): pontuação no-ad, games, sets, tie-break, saque, let, faltas, troca de lado, simples e duplas.
- Fluxo: pré-partida → saque → ponto → fim de game/set/partida.
- Testes cobrindo cada regra e cenários-limite.

**Validação:**
- [x] Partida completa jogável: humano × bot básico, simples (quadra de 4,5 m) ou duplas, formato rápido ou oficial ITF. Menu → partida → resultado → menu, rodando no BlueStacks.
- [x] Testes de regra 100% passando: 77 no total, 27 novos (18 de placar e 9 do juiz com pontos simulados).
- [ ] Checklist de regras ITF revisado por você (abaixo).

**Checklist — ITF Rules of Beach Tennis 2026 (conferido no PDF oficial, vigente desde 01/01/2026):**

| Regra | Implementação | Status |
|---|---|---|
| R1 Quadra 16×8 (duplas) / 16×4,5 (simples); medidas pela borda externa da linha; linha é dentro | `CourtDimensions`, `CourtView` (linhas desenhadas por dentro) | ✅ |
| R1 Rede 1,80 m masc./meninos, 1,70 m fem./meninas/mista | `RuleSet.NetHeight` por categoria | ✅ (o jogo usa feminina/mista por padrão) |
| R6/R7 Melhor de 3 tie-break sets; match tie-break de 10 em 1-1 | `RuleSet.ItfOfficial`, `MatchScore` | ✅ |
| R8a No-Ad: ponto decisivo em 40-40 | `MatchScore` | ✅ |
| R8b Tie-break até 7 com 2 de diferença; saque 1-2-2…; quem sacou primeiro recebe o próximo set | `MatchScore` | ✅ |
| R11 Troca de lado nos games ímpares, no fim de set ímpar, e no TB após o 1º ponto e depois a cada 4 | `MatchScore` + câmera segue o humano | ✅ |
| R15 Saque alterna por game; duplas A1-B1-A2-B2 | `MatchScore` | ✅ (a ordem dentro do time é fixa; a regra deixa o time escolher) |
| R16 Qualquer jogador do time pode receber | Os dois planejam o contato; quem chega primeiro bate | ✅ |
| R17/R18 Saque atrás da linha de fundo, dentro do prolongamento das laterais; **um só saque**; cai em qualquer lugar da quadra adversária | `PlayerSystem.BeginServe`, `PointReferee` | ✅ |
| R18 Na mista, homem saca por baixo | — | ❌ fora do escopo (sem gênero de personagem ainda) |
| R20b Errar a bola ao tentar sacar = falta; deixar cair não é | `ServeMissed` / novo lançamento | ✅ |
| R22 Saque que toca a fita e passa está em jogo | Física (fita) + juiz não marca "let" | ✅ |
| R24b Bola toca a areia do seu lado = perde | `BallLandedInOwnCourt` | ✅ |
| R24c Bola fora = perde quem bateu | `Out` / `ServeOut` | ✅ |
| R24e/j Dois toques do mesmo time | `DoubleTouch` | ✅ |
| R24f/g Tocar a rede ou a quadra adversária; bater antes de a bola passar | Impossível por construção (limites de movimento, planejador de contato) | ✅ |
| R24h Bola toca o corpo | `BodyHit` (cápsula de 28 cm × 1,85 m) | ✅ |
| R14/R24d Bola em poste ou objeto fixo | — | ❌ postes sem colisão (raro) |
| App. I Zona proibida até o saque (3 m fem./mista; 6 m masc.) | `MinNetDistance` liberada quando a bola é golpeada | ✅ |
| App. VI Sets curtos (4 games, TB em 4-4), set com vantagem, match TB de 7 | `RuleSet` | ✅ |
| R29 20 s entre pontos, 90 s na troca | Pausa de 2,2 s (ritmo de jogo mobile) | ⚠️ adaptado de propósito |

**Formatos no menu:** "Rápida" = 1 set curto (4 games, TB em 4-4). Partida de 1 set não é formato oficial ITF, e o menu deixa isso claro. "Oficial ITF" = melhor de 3 com match tie-break de 10.

## Fase 4 — Inteligência artificial (M)
- Posicionamento tático (cobertura de quadra, subir/recuar na rede, divisão com parceiro em duplas).
- Escolha de golpe por situação; erro humano e tempo de reação por perfil de dificuldade (4 níveis).
- IA parceira com comunicação simples ("minha"/"sua") e sem disputar a mesma bola.
- IA usa apenas `PlayerCommand` (mesma interface do humano).

**Validação:**
- [ ] Bot fácil é vencível por iniciante; difícil desafia quem joga bem. *Precisa do seu teste.* A escada foi validada objetivamente com partidas simuladas, bot contra bot (seeds fixas, viram testes de regressão):

  | Confronto (pontos simulados) | Vitórias do mais forte | Golpes por ponto |
  |---|---|---|
  | Médio × Fácil (simples) | 71% | 4,4 |
  | Difícil × Médio (simples) | 78% | 8,7 |
  | Profissional × Difícil (simples) | 77% | 20,8 |
  | Difícil × Médio (duplas) | 68% | 8,7 |
  | Médio × Médio (equilíbrio) | 53% | 5,8 |
- [x] Em duplas, o parceiro IA não "rouba" bola: **0 ticks de conflito** (os dois indo na mesma bola) e **0 "dois toques"** em 120+ pontos simulados. O humano tem prioridade, a menos que a IA chegue claramente antes (0,2 s). O parceiro avisa "MINHA!"/"SUA!" e cobre a metade livre.
- [x] IA (4 bots) < 0,5 ms/frame: **1,5 µs/frame** medidos no editor. A simulação inteira (bola + 4 jogadores + 4 IA + juiz) custa 14,6 µs/frame. *Confirmar em aparelho real.*
- [x] 88 testes EditMode passando (11 novos de IA).

**Como a IA funciona (sem trapaça):** ela vê só o que um jogador vê (a trajetória prevista, a mesma que desenha o anel do humano, e onde cada um está) e age só pelo `PlayerCommand`. A dificuldade muda características humanas:

| Parâmetro | Fácil | Médio | Difícil | Profissional |
|---|---|---|---|---|
| Tempo de reação | 0,30 s | 0,22 s | 0,15 s | 0,10 s |
| Erro de timing (σ) × (1 + velocidade da bola/20) | 100 ms | 70 ms | 45 ms | 28 ms |
| Mira no espaço vazio | 20% | 50% | 80% | 95% |
| Potência | 0,35 | 0,50 | 0,65 | 0,80 |
| Smash no corpo do adversário | 0% | 10% | 25% | 35% |

Tática: smash na bola alta; lob quando os adversários estão na rede; curtinha quando estão no fundo; senão, joga no espaço vazio, com penalidade de risco perto das linhas maior para os menos precisos.

**Problemas de jogabilidade encontrados e corrigidos pela simulação (afetavam o humano também):**
1. 70% dos pontos terminavam na rede. Causa: o mergulho saía baixo. Agora o mergulho gera uma bola defensiva alta, como na vida real.
2. O planejador escolhia o ponto de contato "no limite", então qualquer atraso virava mergulho. Agora ele prefere o primeiro ponto alcançável com folga.
3. Margem sobre a rede: 35 cm para voleio/curtinha, 25 cm para saque, 20 cm para smash (com 35 cm o smash virava bola fofa; com 10 cm ia na rede).
4. A regra anti-travamento marcava "repetir" em qualquer rally acima de 15 s. Agora só marca com a bola parada por 3 s.
5. Nova **potência** no comando (padrão 0,5, que mantém as velocidades anteriores). Voleio de 11 a 20 m/s, smash de 20 a 28 m/s.

**Em aberto:** rallies Profissional × Profissional são longos (~30 golpes). Contra humanos isso deve mudar; recalibrar depois do teste. Os textos de placar e anúncios alocam ~300 B por ponto; vira texto em cache na UI da Fase 6.

## Fase 5 — Personagens e animação (L)

**Em andamento — primeira integração com X Bot:**
- FBX do usuário importado como Humanoid válido, altura visual de 1,80 m, cores por time; aplicado em Match, Training e Sandbox.
- O arquivo fornecido contém apenas uma pose de ~0,033 s. Ainda não há mocap de locomoção ou de golpes.
- Base procedural provisória: ciclo das pernas proporcional à velocidade, preparação e variantes de forehand, backhand, lob, curta, smash e saque. Root motion permanece desligado; a simulação controla a posição.
- Raquete vinculada à mão direita; IK analítico de braços/pernas, com limites anatômicos. O evento real de Hit informa o ponto de contato. Golpes fora do alcance visual não esticam o esqueleto.
- Conferência pelo Unity CLI: rig inicializado e captura em Screenshots/Phase5-pose.png. Os critérios abaixo continuam pendentes; uma captura não valida contato em 95% dos golpes, foot sliding ou custo completo em celular.
- Sincronização antecipada implementada: preparação de saque/smash pelo plano atual; tempo visual acompanha o relógio interpolado da simulação; IK converge antes do Hit e solta gradualmente no follow-through. Planos cancelados deixam de atrair a raquete, e a pausa congela a pose. 99 testes EditMode aprovados (10 novos de seleção, tempo do contato, cancelamento e avatar em 30/60/120 FPS); isso não substitui a avaliação de 95% das rebatidas em partidas reais.
- Equipamento atual: somente o FBX Meshy_AI_Arion_Sunset_Paddle_20261123_generate.fbx indicado pelo usuário, em Art/Equipment/ArionPaddle. As raquetes anteriores e seus materiais/texturas foram removidos do projeto. Modelo sem textura, material URP na cor original, comprimento de 50 cm, origem no cabo e centro de contato em +0,32 m X. Aplicado em Match e Training e preservado na reconstrução de cenas. Malha com 189.830 triângulos; otimização para celular ainda pendente.
- Correção de postura/empunhadura: instâncias do X Bot agora têm transform local zerado sob o pivô do corpo. Um deslocamento indevido de ~4,3 m fazia o IK tentar alcançar uma origem distante e aparentar uma pose sentada. A raquete usa um apoio calibrado na palma (não no punho), orientação da mão alinhada ao cabo e dedos fechados. Conferência em Screenshots/Posture-fixed.png e Grip-fixed.png; testes cobrem as cenas salvas, pés após translação/rotação e encaixe estável durante a rebatida.
- Olhar e LOD procedural: cabeça/pescoço acompanham a bola em voo com limites de 65° na horizontal e 30° na vertical, suavização pelo tempo simulado e retorno ao centro quando a bola fica inativa ou atrás. Binding da bola integrado no treino e nas partidas. Animação distante usa cadência de 30/15 Hz conforme distância/qualidade; preparação, contato, golpes e recuperação ignoram a redução. Posição da raiz, física e regras seguem em sua frequência original. Isto reduz atualizações da pose; não reduz triângulos nem substitui medição de skinning no celular.
- **Animações da Mixamo integradas (22/11/2026):** 9 clipes importados como Humanoid com o avatar do X Bot (`XBotAnimationSetup`, menu Beach Tennis/Setup/X Bot Animations). Animator com Blend Tree 2D de locomoção (parado, correr, correr de costas, deslocamento lateral) movida pela **velocidade local da simulação**, sem root motion. Mergulho → levantar disparado quando o jogador entra em "Recovering", espelhado para o lado do contato (0,55 s + 0,75 s; `DiveRecovery` ajustado para 1,3 s para casar com a animação; escada de IA revalidada). Vitória/decepção no fim de cada game, resetadas a cada novo ponto.
- Golpes continuam procedurais + IK **por cima** do Animator (braço da raquete, mão livre durante o golpe, dedos, olhar). Não há clipes de tênis na Mixamo. Para golpes de mocap de verdade, o caminho é mocap específico de beach tennis.
- LOD de malha: raquete (189.830 tris) e X Bot (49 mil tris) com LOD gerado na importação; duplas caíram de 1,9 mi para ~91 mil triângulos (149 mil no BlueStacks).
- Medido no BlueStacks: duplas animadas a ~60 FPS, 400 MB, GC só nos textos de ponto (~450 B por ponto).
- **Em aberto:** a espera usa "Scary Clown Idle" (pose estranha, trocar por "Ready Idle"); o skinning ainda processa todos os vértices (o LOD reduz triângulos, não vértices) e precisa ser medido em aparelho real; validar contato da raquete em 95% dos golpes e foot sliding em partidas reais.
- Rig humanoide, importação de mocap, Blend Tree de locomoção, set de golpes com evento de contato.
- `ShotAnimationSelector` + time-warp de contato + IK de braço/raquete e olhar (Animation Rigging).
- Transições suaves, recuperação de posição, reações (comemoração, frustração).
- LOD de animação (update rate, rig desligado em LOD baixo).

**Validação:**
- [ ] Raquete visualmente toca a bola em ≥ 95% das rebatidas.
- [ ] Sem foot sliding perceptível na locomoção.
- [ ] 4 personagens animados ≤ 2 ms CPU no tier mínimo.

## Fase 6 — Câmera, HUD e UX de partida (S)
- `CameraDirector` com Cinemachine: câmera de jogo, saque, troca de lado, destaque.
- HUD (placar, indicador de golpe, feedback de timing), safe areas, pausa.
- Menus básicos (UI Toolkit): jogar, modo, dificuldade, opções gráficas.

**Validação:**
- [ ] Bola sempre legível na câmera de jogo (teste em tela de 6"). *No BlueStacks a bola, o rastro e a linha de trajetória ficam legíveis com a câmera nova; falta um celular de 6".*
- [ ] HUD < 0,5 ms CPU; nenhum rebuild de Canvas por frame sem necessidade. *Textos só mudam por ponto; medir em aparelho.*

**Entregue (23/11/2026):**
- `CameraDirector` (Cinemachine 3): câmera de jogo atrás da sua linha de fundo, que acompanha 35% do deslocamento lateral e avança quando você sobe à rede, com o foco puxado levemente para a bola; câmera de saque mais baixa e próxima; close no fim de cada game; **corte seco na troca de lado** (uma transição atravessaria a rede). Usada na partida e no treino.
- Pausa: botão II, botão voltar do Android e **pausa automática ao sair do app**. Congela simulação e tempo; Continuar / Sair para o menu.
- Menu em **UI Toolkit** (`MainMenuView`, `PanelSettings` em Resources/UI): Simples rápida, Duplas rápida, Simples oficial ITF, Treino, Opções.
- Opções salvas (`GameSettings`, PlayerPrefs) e aplicadas na hora: dificuldade, linha da trajetória, qualidade gráfica (Baixa/Média/Alta/Ultra), 60/120 FPS (avisa quando a tela não suporta), categoria (Feminina/Mista: rede 1,70 m e zona de 3 m; Masculina: 1,80 m e 6 m).
- Verificado no BlueStacks: toque no UI Toolkit, opções aplicadas na partida (rede 1,80 m na masculina), pausa e retomada.
- **Decisão:** os textos de placar e anúncios continuam alocando ~450 B **por ponto**, não por frame. Com GC incremental isso não trava; não compensa a complexidade de texto sem alocação agora.
- Pendente: tela de resultado e pausa ainda em uGUI (migrar para UI Toolkit junto com o visual da Fase 7); HUD de placar com visual final.

## Fase 7 — Produção visual (L, depende de arte)
- Quadra de areia (shader, pegadas, marcas da bola), rede, linhas.
- Ambiente de praia: céu, água, vegetação, estruturas, torcida (VAT instanciada).
- Iluminação baked + APV, reflection probes, ciclo de horários pré-definidos (manhã, pôr do sol, noite com holofotes).
- VFX: impacto da bola, areia, mergulho, smash. Pós-processamento por tier.
- Personagens finais (pele, cabelo, roupas) com LODs.

**Validação:**
- [ ] Orçamentos do doc 01 §4.1 cumpridos em cada tier, em aparelho, após 20 min.
- [ ] Revisão visual sua comparando com referências aprovadas.

## Fase 8 — Otimização, áudio e matriz de aparelhos (M)
- Áudio (impactos, areia, torcida, ambiente, música, voz do árbitro) com mixer e pools.
- `QualityManager` com tabela de aparelhos + ajuste dinâmico + Adaptive Performance.
- Testes em matriz de 6–10 aparelhos (cloud device farm ou físicos).
- Shader warm-up, variantes, tamanho de build, tempo de loading.

**Validação:**
- [ ] 60 FPS sustentados (p95 frame time ≤ 18 ms) em todo o tier mínimo da matriz.
- [ ] Nenhum crash/ANR em 2 h de sessões automatizadas.

## Fase 9 — Conteúdo, meta e lançamento (M–L)
- Addressables com conteúdo remoto, progressão/carreira simples, customização de personagem, salvamento.
- Tutorial, localização (PT-BR, EN, ES, IT), acessibilidade.
- Analytics de desempenho e crash reporting; preparo das lojas (AAB, TestFlight).

## Fase 10 — Multiplayer online (L, pós-lançamento ou paralelo)
- Escolha de transporte/serviço (ver doc 01 §3.6), servidor autoritativo headless, predição, matchmaking.

---

### Marco de MVP jogável
Fases 0–4 com gray-box = **jogo completo jogável com cápsulas**. É o ponto ideal para decidir investimento em arte/mocap com base em diversão comprovada, não em suposição.







