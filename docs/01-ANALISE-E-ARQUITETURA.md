# Beach Tennis 3D Mobile — Análise, Arquitetura e Decisões Técnicas

> Documento vivo. Versão 0.1 — 22/11/2026. Status: **aguardando validação** antes da Fase 0.

---

## 1. Análise de requisitos e limitações

### 1.1 O que o esporte impõe ao design (e por que isso muda a engenharia)

O Beach Tennis tem uma característica que define quase toda a arquitetura: **a bola não pode quicar na areia durante o ponto**. Se quicou, o ponto acabou. Consequências diretas:

| Característica do esporte | Impacto técnico |
|---|---|
| Todo golpe é aéreo (voleio, smash, lob, saque) | Não existe "groundstroke". O set de animações é de voleios em alturas diferentes, smashes, bandejas, lobs e mergulhos. |
| Quique na areia = fim do ponto | A física de quique é **cosmética e de arbitragem** (marca na areia, partículas, decisão dentro/fora), não de jogabilidade. Pode ser simples e barata. |
| Trocas rápidas perto da rede | Latência de input é crítica. Janela de reação curta → o sistema precisa **prever a trajetória** com precisão (para IA, auto-posicionamento e sincronização de animação). |
| Bola despressurizada, sem quique útil | Arrasto aerodinâmico e efeito (Magnus) importam mais que o quique. |
| Quadra 16 m × 8 m (duplas) / 16 m × 4,5 m (simples); rede 1,80 m (masculino) / 1,70 m (feminino e misto), conferido nas regras ITF 2026 | Câmera e espaço de cena compactos → favorece qualidade visual concentrada numa área pequena. |
| Areia funda | Movimento do jogador com aceleração menor, passos curtos, mergulhos. Animação de locomoção precisa "pesar". |

> As regras oficiais (ITF Beach Tennis) serão **dados configuráveis** (`RuleSet` em ScriptableObject), não código fixo. Detalhes como pontuação sem vantagem (no-ad), tie-break, let no saque e rotação de saque serão conferidos contra o regulamento ITF vigente na Fase 3, com testes automatizados para cada regra.

### 1.2 Limitações reais de dispositivos móveis

| Limitação | Consequência no projeto |
|---|---|
| **Térmica** — celulares reduzem clock após 5–15 min de carga alta | O alvo não é "pico de FPS", é **FPS sustentado após 20 min**. Medimos com o aparelho quente. Orçamento de GPU com ~30% de folga. |
| **Bandwidth de memória** (GPUs tile-based: Mali, Adreno, Apple) | Evitar passes full-screen caros, MSAA resolvido em tile, nada de buffers extras desnecessários (Depth/Opaque texture só se usados). |
| **Fragmentação Android** | Tiers de qualidade automáticos por perfil de GPU/RAM + ajuste em runtime pelo frame time medido. |
| **Bateria** | 60 FPS como padrão; 120 FPS como opção nos aparelhos capazes, nunca forçado. Menus a 30 FPS. |
| **RAM** (OS mata o app acima de ~50–60% da RAM em aparelhos de 4 GB) | Orçamento total ≤ 1,2 GB no tier médio; texturas ASTC; Addressables para carregar só o cenário/personagens da partida. |
| **Input por toque** | Sem precisão de mouse, dedo cobre a tela. Controles baseados em **intenção** (direção + tipo de golpe), com assistência de posicionamento. |
| **Build iOS** | Exige macOS + Xcode. Precisamos de um Mac (ou serviço de build em nuvem). |

### 1.3 Dispositivos de referência (a validar com você)

| Tier | Android (referência) | iOS (referência) | Alvo |
|---|---|---|---|
| Mínimo | Snapdragon 7 Gen 1 / Exynos 1380 (ex.: Galaxy A54), 6 GB | iPhone 11 (A13) | 60 FPS, qualidade "Média" |
| Alto | Snapdragon 8 Gen 2+ | iPhone 13 Pro e superiores | 60 FPS "Alta" ou 120 FPS "Média" |
| Topo | Snapdragon 8 Elite / Dimensity 9400 | iPhone 15 Pro e superiores | 120 FPS "Alta" / 60 FPS "Ultra" |

Abaixo do tier mínimo: o jogo roda em "Baixa" a 30–60 FPS, sem garantias de qualidade premium. SO mínimo: Android 10 (API 29, Vulkan) e iOS 16.

---

## 2. Comparação de tecnologias e escolhas

### 2.1 Versão da Unity
**Escolha: Unity 6 LTS (a LTS mais recente da linha 6 no dia da criação do projeto — hoje, 6.3 LTS ou superior).**
- LTS = 2 anos de correções, sem features experimentais quebrando o projeto.
- Unity 6 trouxe ganhos relevantes para mobile: Render Graph no URP, GPU Resident Drawer, Spatial-Temporal Post-processing (STP), Adaptive Probe Volumes, Cinemachine 3, melhorias em Vulkan.
- **Rejeitado:** versões Tech Stream (instáveis para produção longa); 2022 LTS (fim de suporte, sem Render Graph).

### 2.2 Render pipeline
**Escolha: URP (Forward+).**

| Opção | Veredito |
|---|---|
| **URP** | ✅ Feito para mobile, SRP Batcher, Shader Graph, controle fino de cada pass, escala do celular médio ao topo de linha com o mesmo conteúdo. |
| HDRP | ❌ Não suporta mobile. |
| Built-in | ❌ Legado, sem novas features, SRP Batcher indisponível. |

Por que **Forward+** e não Forward: permite várias luzes pontuais (holofotes de partida noturna, por ex.) sem limite de 8 por objeto e é pré-requisito do GPU Resident Drawer. Custo: um compute de light culling — barato em GPUs Vulkan/Metal modernas. Será **medido na Fase 0**; se o tier mínimo sofrer, o tier "Baixa" usa um Renderer Forward simples (URP permite renderers diferentes por tier).

### 2.3 Física
**Escolha: simulação própria para a bola + controlador cinemático próprio para os jogadores. PhysX apenas para consultas espaciais auxiliares (raycasts de câmera/terreno), se necessário.**

Por que **não** usar Rigidbody/PhysX na bola:
1. **Previsibilidade:** a IA, o auto-posicionamento e a sincronização das animações precisam saber *onde e quando* a bola estará daqui a 0,5–1,5 s. Com simulação própria, a mesma função que move a bola gera a previsão — resultado idêntico, garantido.
2. **Rede futura:** uma rebatida vira um evento pequeno (posição, velocidade, spin, tick). Todos os clientes reproduzem a mesma trajetória. Com PhysX isso não é determinístico entre plataformas.
3. **Controle de design:** efeito Magnus e arrasto calibrados para uma bola de beach tennis (despressurizada), com parâmetros em ScriptableObject para tuning.
4. **Custo:** uma bola com integrador semi-implícito a tick fixo custa microssegundos; sem tunneling (verificamos rede/areia por varredura de segmento a cada passo).

Modelo físico da bola: gravidade + arrasto quadrático + força de Magnus (spin) + decaimento de spin. Colisões analíticas com: plano da areia (quique cosmético com alta absorção e marca), rede (plano vertical com faixa superior — trata "let" e bola que bate na fita), postes e limites.

**Contato raquete-bola:** não é colisão física. Usamos um **modelo de intenção**: no instante do contato, o sistema recebe *tipo de golpe + alvo desejado + potência + qualidade do timing + atributos do jogador*, e um *solver* calcula a velocidade/spin iniciais que levam a bola ao alvo (com erro proporcional ao timing ruim). É assim que jogos de tênis comerciais garantem controle preciso no toque. Colisão real raquete-bola a 30 m/s com 60 Hz é instável e incontrolável para o jogador.

**Jogadores:** controlador cinemático (posição/velocidade calculadas no tick fixo, sem Rigidbody), com aceleração e frenagem que simulam areia, limites de quadra e separação entre jogadores. Mais barato e mais preciso que CharacterController do PhysX para esse caso.

**Rejeitado:** Unity Physics/Havok (DOTS) — ganho zero para ~5 objetos dinâmicos, custo alto de complexidade.

### 2.4 Tick de simulação
Simulação a **tick fixo de 120 Hz** (bola precisa de passo curto em velocidades altas), desacoplada do framerate. A apresentação (personagens, bola visual, câmera) **interpola** entre os dois últimos estados. Resultado: comportamento idêntico em 30, 60 ou 120 FPS e pronto para rede.

### 2.5 Animação
**Escolha: Mecanim (Animator) + Playables pontuais + Animation Rigging com moderação.**

- **Locomoção:** Blend Tree 2D (direção × velocidade) com passos laterais (split-step, shuffle), corrida e frenagem na areia.
- **Golpes:** cada golpe é escolhido pela **posição de contato prevista** em relação ao corpo (altura × lado × distância) e pelo **tempo até o contato**. O clipe tem um *evento de contato* marcado; o sistema ajusta a velocidade de reprodução (time-warp de ±20%) para que o frame de contato coincida com a chegada da bola. Isso é o que faz o golpe "acertar de verdade" visualmente.
- **Animation Rigging:** IK de braço/punho (Two Bone IK + Multi-Aim) ligado **só durante a janela de contato**, para levar a raquete exatamente até a bola. Cabeça olha para a bola (Multi-Aim). Rigs desligados em jogadores distantes / tier Baixo.
- **Rejeitado:** Motion matching de terceiros — excelente qualidade, mas custo de CPU e de dados (dezenas de minutos de mocap) alto para mobile. Reavaliar após a Fase 5 se a qualidade não for suficiente.
- **Fonte das animações:** mocap (comprado/contratado). Sem mocap de qualidade, nenhum código salva a animação — este é um risco de produção, não técnico.

### 2.6 Input
**Escolha: Input System (novo), com Enhanced Touch.**
- Joystick virtual flutuante (lado esquerdo) para movimento + botões contextuais / gesto de swipe (lado direito) para tipo e direção do golpe.
- Assistência configurável: auto-posicionamento leve em direção ao ponto de contato previsto (em vez de exigir precisão de toque).
- **Buffer de input:** o toque é registrado com timestamp e aplicado no tick correto, mesmo que chegue entre frames — reduz a latência percebida.
- Suporte a gamepad gratuito (mesmo Input Actions).

### 2.7 Câmera
**Escolha: Cinemachine 3.**
- Câmera principal estilo transmissão, atrás do jogador, com enquadramento que mantém bola + jogador + lado adversário visíveis (Target Group com pesos dinâmicos).
- Câmeras de saque, troca de lado, pontos de destaque e replay como câmeras virtuais com blends.
- Regras: sem rotação brusca durante o ponto, sem shake que atrapalhe leitura; shake só em smash forte, curto e configurável.

### 2.8 Carregamento de conteúdo
**Escolha: Addressables** desde o início, com grupos simples:
- `Core` (sempre carregado): UI, bola, sistemas.
- `Arena_<nome>`: cada praia/ambiente.
- `Character_<id>`: personagens e roupas.
Permite baixar ambientes/roupas depois da instalação (APK/AAB < 150 MB inicial) e descarregar o que não está em uso. Cenas de ambiente usam lighting baked carregado junto.

### 2.9 UI
**Escolha: UI Toolkit para menus; uGUI para o HUD de partida** (decisão a confirmar na Fase 6 com medição).
- UI Toolkit: layout flexível, estilos (USS) para uma interface moderna, bom para muitas telas.
- HUD in-game: poucos elementos, atualizados todo frame → uGUI com Canvas separado para estático e dinâmico tem custo previsível e é testado em mobile há anos.
- Safe areas (notch/ilha dinâmica) tratadas desde o primeiro layout.

### 2.10 Outros
| Área | Escolha | Motivo |
|---|---|---|
| Scripting backend | IL2CPP, ARM64 | Desempenho e exigência das lojas. |
| Gráficos Android | Vulkan (fallback GLES 3.2 só se medição exigir) | Menor overhead de CPU por draw call. |
| Gráficos iOS | Metal | Único caminho. |
| Texturas | ASTC (6×6 cor, 4×4 normais/UI) | Padrão moderno em ambas plataformas. |
| Áudio | Unity Audio + Audio Mixer (Wwise/FMOD só se necessário) | Suficiente para o escopo; reavaliar se houver áudio adaptativo complexo. |
| Térmica Android | Adaptive Performance (provedor Android/Samsung) | Reação a throttling antes do FPS cair. |
| Frame pacing | Optimized Frame Pacing (Android) + `Application.targetFrameRate` | Evita micro-stutter. |
| Testes | Unity Test Framework (EditMode para a simulação, PlayMode para integração) | Simulação pura em C# é testável sem cena. |
| Versionamento | Git + Git LFS (ou Unity Version Control se a equipe tiver artistas não técnicos) | |
| ECS/DOTS | ❌ Não usar | Poucas entidades; complexidade sem ganho. |
| Framework de DI (Zenject/VContainer) | ❌ Inicialmente não | Composição manual num único *composition root* é suficiente e mais legível; reavaliar se o grafo crescer. |

---

## 3. Arquitetura do projeto

### 3.1 Princípio central: simulação separada da apresentação

```
┌──────────────────────────────────────────────────────────────────┐
│  APRESENTAÇÃO (MonoBehaviours, Unity)                            │
│  Views de jogador/bola · Animação · Câmera · VFX · Áudio · UI    │
│  Lê estados da simulação e interpola. Nunca altera regras.       │
└──────────────▲──────────────────────────────┬────────────────────┘
               │ eventos (BallHit, PointWon…)  │ comandos (input)
┌──────────────┴──────────────────────────────▼────────────────────┐
│  SIMULAÇÃO (C# puro, sem UnityEngine exceto Mathematics)         │
│  MatchSimulation · BallSim · PlayerSim · ShotSolver · Rules · AI │
│  Tick fixo 120 Hz, determinística por entrada, 100% testável     │
└──────────────────────────────────────────────────────────────────┘
```

Por que isso importa:
- **Testes**: regras, física da bola e IA testadas em milissegundos no editor, sem abrir cena.
- **Multiplayer futuro**: a simulação pode rodar num servidor ou ser reconciliada no cliente sem reescrever o jogo.
- **Desempenho**: a simulação usa structs e arrays pré-alocados; zero alocação por frame.
- **Manutenção**: trocar um modelo 3D ou animação nunca quebra uma regra de pontuação.

### 3.2 Assemblies (asmdef) e dependências

```
BeachTennis.Core          ← matemática, tipos base, eventos, pools (sem dependência)
BeachTennis.Simulation    ← Core           (bola, jogadores, golpes, regras, partida)
BeachTennis.AI            ← Core, Simulation
BeachTennis.Input         ← Core           (+ Input System)
BeachTennis.Presentation  ← Core, Simulation (views, animação, câmera, VFX, áudio)
BeachTennis.UI            ← Core, Simulation
BeachTennis.Game          ← todos          (composition root, fluxo de telas, boot)
BeachTennis.Networking    ← Core, Simulation  (futuro)
BeachTennis.Tests.*       ← testes por módulo
BeachTennis.Editor        ← ferramentas de editor (visualizador de trajetória, tuning)
```

Regra: as setas só apontam "para baixo". `Simulation` nunca referencia `Presentation`. Assemblies separados também reduzem o tempo de recompilação.

### 3.3 Sistemas principais

| Sistema | Responsabilidade | Interface central (rascunho) |
|---|---|---|
| `MatchSimulation` | Orquestra o tick: input → jogadores → golpes → bola → regras → eventos | `Tick(in InputFrame[] inputs)` |
| `BallSimulation` | Integra trajetória, detecta rede/areia/fora, emite eventos | `Step(ref BallState, float dt)`; `Predict(BallState, Span<BallState>)` |
| `ShotSolver` | Converte intenção de golpe em velocidade/spin iniciais | `Solve(ShotIntent, PlayerAttributes, TimingQuality) → BallLaunch` |
| `PlayerSimulation` | Locomoção na areia, alcance, janela de golpe | `Step(ref PlayerState, in PlayerCommand, float dt)` |
| `HitDetection` | Decide se e quando um jogador alcança a bola (via trajetória prevista) | `FindContactWindow(...)` |
| `RulesEngine` | Pontuação, saque, faltas, troca de lado, simples/duplas | `OnBallEvent(...) → RuleOutcome` |
| `AIController` | Gera `PlayerCommand` para adversários/parceiro: posicionamento + escolha de golpe + erro humano por dificuldade | `Think(in MatchView) → PlayerCommand` |
| `PlayerView` | Interpola posição, dirige Animator, ativa IK no contato | — |
| `ShotAnimationSelector` | Escolhe clipe por ponto de contato e aplica time-warp | — |
| `CameraDirector` | Ativa câmeras Cinemachine por estado da partida | — |
| `QualityManager` | Detecta tier, aplica URP asset/settings, reage a térmica e frame time | — |
| `ContentLoader` | Addressables: carrega/descarrega arena e personagens | — |

A IA usa **exatamente a mesma interface de comando** que o jogador humano (`PlayerCommand`). Ela não "trapaceia" com acesso a estados internos além do que um jogador veria — isso também deixa os bots substituíveis por jogadores remotos no online.

### 3.4 Dados e configuração
Tudo que é tuning vira ScriptableObject versionado: `BallPhysicsConfig`, `CourtConfig`, `RuleSet`, `ShotConfig` (por tipo de golpe), `PlayerAttributes`, `AIDifficultyProfile`, `QualityTierProfile`. Designers ajustam sem tocar em código; testes usam os mesmos assets.

### 3.5 Estrutura de pastas

```
Assets/
  _Project/
    Art/            (Characters, Environment, Materials, Shaders, VFX, UI)
    Audio/
    Config/         (ScriptableObjects de tuning)
    Prefabs/
    Scenes/         (Boot, Menu, Match, Arenas/, Sandbox/, Benchmarks/)
    Scripts/        (uma pasta por assembly acima)
    Settings/       (URP assets por tier, Input Actions, Addressables)
    Tests/
  Plugins/          (terceiros, isolados)
```

### 3.6 Estratégia de multiplayer online (futuro, mas decidido agora)
- **Modelo:** servidor autoritativo + predição no cliente do próprio jogador. A bola é reproduzida localmente a partir dos **eventos de rebatida** (pequenos e raros: ~1 por segundo), e não sincronizada quadro a quadro.
- **Por que já importa:** tick fixo, simulação em C# puro, comandos de input serializáveis e IA usando a mesma interface de comando — tudo isso é o que torna o online viável sem reescrita.
- **Transporte/serviço:** decisão adiada para a fase online, comparando Netcode for GameObjects + Unity Multiplayer Services/Relay, Photon Fusion e servidor dedicado próprio. Critérios: latência regional (Brasil), custo por CCU, suporte a servidor autoritativo headless.

---

## 4. Estratégia de otimização desde o início

### 4.1 Orçamentos por frame (tier Médio, 60 FPS = 16,6 ms)

| Recurso | Orçamento |
|---|---|
| CPU main thread | ≤ 8 ms (simulação ≤ 1 ms, animação ≤ 2 ms, resto Unity/UI) |
| GPU | ≤ 11 ms (folga para térmica) |
| Draw calls (SetPass) | ≤ 100 SetPass, ≤ 250 batches |
| Triângulos visíveis | ≤ 350 k (Médio) / ≤ 700 k (Alto) |
| Personagem LOD0 | 18–25 k tris, 1 material principal + cabelo, 2–3 texturas 2K ASTC |
| Ossos influenciando vértice | 4 (Médio), 2 (Baixo) |
| Memória de texturas | ≤ 350 MB (Médio) |
| Memória total | ≤ 1,2 GB (Médio) |
| GC alloc durante a partida | **0 bytes/frame** |
| Tempo de carregamento da partida | ≤ 6 s no tier mínimo |

Esses números entram num **painel de debug in-game** e numa cena de benchmark automatizada desde a Fase 0. Uma regressão de orçamento é tratada como bug.

### 4.2 Iluminação e sombras
- Sol: **uma** luz direcional em tempo real. Sombras dinâmicas só onde importam (jogadores, bola, rede), com distância de sombra curta (~30 m), 1–2 cascatas, soft shadows apenas no tier Alto+.
- Ambiente: **lightmaps baked** + Adaptive Probe Volumes (ou Light Probes no tier Baixo) para iluminar os personagens de forma coerente com o cenário.
- Reflexos: reflection probes baked; água sem reflexo planar (caríssimo) — usa probe + normal map animado + fresnel.
- Bola: sombra própria via blob/decal simples além da sombra real — é a principal pista de profundidade para o jogador. **Isso é jogabilidade, não estética.**

### 4.3 Materiais e shaders
- PBR via URP Lit / Shader Graph, sempre compatíveis com SRP Batcher.
- Shaders customizados para: areia (detail normal + sparkle barato + pegadas), água (vertex waves simples + espuma por profundidade só no Alto), vegetação (vento por vértice), pele (subsurface aproximado barato).
- **Pegadas e marcas na areia:** render texture de deformação local à quadra (tier Médio+), decal simples no tier Baixo.
- Controle rigoroso de variantes de shader (strip de keywords não usados) — causa clássica de builds lentas e hitch ao carregar.
- Shader warm-up / PSO cache na tela de carregamento para evitar travadas na primeira rebatida.

### 4.4 Geometria e cena
- LOD Groups em personagens, torcida e vegetação; impostores/billboards para elementos distantes (coqueiros, prédios).
- Occlusion culling: cena de praia é aberta e tem pouca oclusão — **ganho esperado pequeno**; será medido, não assumido. Frustum culling + LOD + distância de culling por camada (`layerCullDistances`) trarão mais.
- Torcida: GPU instancing com animação por vertex texture (VAT), não Animators.
- Static batching / GPU Resident Drawer para cenário; testado por tier.

### 4.5 Pós-processamento (por tier)
| Efeito | Baixo | Médio | Alto | Ultra |
|---|---|---|---|---|
| Tonemapping + color grading (LUT) | ✅ | ✅ | ✅ | ✅ |
| Bloom (resolução reduzida) | ❌ | ✅ | ✅ | ✅ |
| Anti-aliasing | FXAA | MSAA 2× | MSAA 4× | MSAA 4× |
| Vinheta / profundidade de campo | ❌ | ❌ | só replays | só replays |
| SSAO | ❌ | ❌ | ❌ | avaliar |
| Escala de render | 0,8 | 0,9–1,0 | 1,0 | 1,0 |

Resolução dinâmica / STP avaliados na Fase 7 com medição real.

### 4.6 CPU e código
- Zero alocação no loop (structs, arrays pré-alocados, pools para VFX/áudio, sem LINQ/closures/string concat no gameplay).
- Sem `Update()` espalhados: um único ponto de tick chama os sistemas em ordem definida.
- Burst + Jobs apenas onde a medição mostrar ganho (ex.: torcida, deformação da areia) — não por padrão.
- Animators de jogadores distantes com `cullingMode` e update rate reduzido.

### 4.7 Ajuste dinâmico de qualidade
`QualityManager` escolhe o tier inicial por perfil do aparelho (GPU, RAM, SO) a partir de uma tabela, depois observa o frame time real nos primeiros pontos e reage a avisos térmicos: primeiro reduz escala de render, depois sombras/pós, nunca a taxa de simulação.

---

## 5. Riscos principais

| Risco | Severidade | Mitigação |
|---|---|---|
| Animações de baixa qualidade (sem mocap) | Alta | Orçar mocap cedo; protótipo com pacote comprado; o sistema de contato funciona com qualquer clipe que tenha evento de contato. |
| Personagens 3D premium (custo/tempo de arte) | Alta | Definir fonte de arte na Fase 0 (compra, freelancer, geração assistida + retoque). Gray-box até a Fase 7. |
| "Feel" do controle no toque | Alta | Fases 1–2 dedicadas a isso, com testes em aparelho real a cada iteração. |
| Térmica derrubando FPS | Média | Orçamentos com folga, medição em aparelho quente, Adaptive Performance. |
| Build iOS sem Mac | Média | Definir Mac/serviço de build na Fase 0. |
| Escopo (duplas + IA + online) | Média | Online fora do MVP; arquitetura preparada, implementação depois. |
