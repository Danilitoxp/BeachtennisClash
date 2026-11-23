# Beach Tennis 3D (Unity 6.3 LTS · URP · Android)

Documentação de projeto:
- [docs/01-ANALISE-E-ARQUITETURA.md](docs/01-ANALISE-E-ARQUITETURA.md) — decisões técnicas, arquitetura, orçamentos de desempenho
- [docs/02-ROADMAP.md](docs/02-ROADMAP.md) — fases e critérios de validação

## Requisitos
- Unity **6000.3.29f1** com módulo Android (SDK/NDK/OpenJDK)
- Unity CLI (`%LOCALAPPDATA%\Unity\bin\unity.exe`)

## Estrutura
```
Assets/_Project/
  Scripts/Core          C# puro: relógio de simulação (tick fixo 120 Hz), scheduler
  Scripts/Simulation    C# puro: quadra, (bola, jogadores, regras nas próximas fases)
  Scripts/Presentation  MonoBehaviours visuais, HUD de desempenho
  Scripts/Game          Composition root (AppRoot), runner da simulação, FPS, boot
  Scripts/Editor        Ferramentas: setup, níveis de qualidade, cenas gray-box, build
  Settings/Rendering    URP_Low / Medium / High / Ultra + renderers Forward / Forward+
  Scenes/               Boot (índice 0) → Match_Graybox
  Tests/EditMode        Testes da simulação
```

## Menu "Beach Tennis" no Editor
| Item | O que faz |
|---|---|
| Setup / Apply Player Settings | IL2CPP, ARM64, Vulkan+GLES3, API 29+, frame pacing, ASTC, paisagem |
| Setup / Build Quality Tiers | Recria os 4 níveis de qualidade e os assets URP |
| Setup / Build Graybox Scenes | Recria as cenas Boot e Match_Graybox a partir de `CourtDimensions` |
| Build / Android Development APK (Device) | Gera `Builds/Android/BeachTennis-dev.apk` (Vulkan, API 29+) para celular real |
| Build / Android Development APK (Emulator, GLES3) | Gera `Builds/Android/BeachTennis-emulator.apk` (OpenGL ES 3, API 28+) para BlueStacks |

## Partida (Fase 3) — cena inicial atual (`Match`)
Menu: **1** Simples rápida · **2** Duplas rápida · **3** Simples oficial ITF · **4** Treino · **5** Dificuldade (Fácil/Médio/Difícil/Profissional).
Você é o vermelho; os demais são IA (Fase 4). Em duplas, o parceiro (laranja) grita "MINHA!"/"SUA!".

Balanceamento sem tela: `BeachTennis.AI.HeadlessMatch` joga pontos IA × IA na simulação real
(ex.: `new HeadlessMatch(1, AiProfile.For(Difficulty.Hard), AiProfile.For(Difficulty.Medium), seed).PlayPoints(100)`). Regras ITF 2026 (ver checklist em `docs/02-ROADMAP.md`).
- Saque: **apertar e segurar SACAR** lança a bola; **soltar** bate. Só há um saque.
- Até o saque, ninguém entra na zona proibida (3 m da rede).
- A câmera troca de lado junto com você.
- No treino, o botão "voltar" do Android (ou Esc) retorna ao menu.

## Treino (Fase 2) — cena `Training`
Você (vermelho, lado sul) devolve bolas de uma máquina, ou treina saque.

| Ação | Toque | Teclado |
|---|---|---|
| Mover | joystick flutuante (metade esquerda) | WASD / setas |
| Golpe (voleio/smash) | segurar e soltar **GOLPE** | segurar e soltar J |
| Lob | segurar e soltar **LOB** | K |
| Curtinha | segurar e soltar **CURTA** | L |
| Mirar | joystick enquanto segura um botão | WASD enquanto segura |
| Receber / Sacar | tocar no painel da direita | M |
| Mostrar trajetória | — | T |

Segurar = o jogador corre até o ponto de contato (anel verde na areia). Soltar = bater.
Ideal: soltar um pouco **antes** de a bola chegar.

Ajuste ao vivo: `Assets/_Project/Settings/Physics/Player.asset` e `BallPhysics.asset`.

## Sandbox "máquina de bolas" (Fase 1)
Cena `Sandbox_BallMachine` (não é mais a cena inicial). Uma máquina do lado norte lança todos os tipos de golpe
(saque, lob, voleio, smash, curtinha, efeito lateral) para o lado da câmera, com a trajetória prevista
desenhada e marcas na areia (verde dentro, vermelho fora).

| Ação | Toque | Teclado |
|---|---|---|
| Lançar o próximo golpe | tocar na quadra | Espaço |
| Câmera de fundo / lateral | tocar no painel da direita | C |
| Escolher golpe | — | 1–6 |
| Liga/desliga automático | — | A |
| Mostrar/ocultar trajetória | — | T |

Ajuste da física ao vivo: abra `Assets/_Project/Settings/Physics/BallPhysics.asset` no Inspector durante o Play Mode.

## HUD de desempenho
Ativo no Editor e em development builds. **F1** ou **toque com 3 dedos** liga/desliga.
A cada 5 s grava um resumo no log (`[Perf] fps=… frame_max=… hitches=… gc_frames=…`). Para um teste longo:
```powershell
adb logcat -s Unity:* | Select-String "\[Perf\]"
```

> Valores medidos no Editor e em emuladores **não** representam desempenho em aparelho real.

## Instalar o APK
**BlueStacks** (instância Pie64, ADB ativado nas configurações; porta em `bluestacks.conf` → `status.adb_port`):
```powershell
adb connect 127.0.0.1:5556
adb -s 127.0.0.1:5556 install -r Builds\Android\BeachTennis-emulator.apk
```
Use sempre o APK **emulator** no BlueStacks: a instância é Android 9 (API 28), e o build Vulkan de desenvolvimento
quebra no driver de emuladores x86 com tradução ARM.

**Celular real** (depuração USB ativada):
```powershell
adb install -r Builds\Android\BeachTennis-dev.apk
```
