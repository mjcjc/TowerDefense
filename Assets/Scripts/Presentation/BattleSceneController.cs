using UnityEngine;
using UnityEngine.SceneManagement;
using TowerDefense;

public class BattleSceneController : MonoBehaviour
{
    private RunController run;
    private bool title=true, showDirector;
    private int selected=-1, moveFrom=-1;
    private int pressedSlot = -1;
    private Vector2 pressPosition;
    private bool draggingTower;
    private double accumulated;
    private float coreHitUntil;
    private string message="";
    private Font font;
    private static AudioSource calmMusic;
    private AudioSource battleMusic, activeMusic;
    private AudioSource impactAudio;
    private AudioClip frostImpact, promotionChime;
    private float musicVolume, effectVolume;
    private bool soundSettingsChanged;
    private const string MusicVolumeKey = "TowerDefense.MusicVolume";
    private const string EffectVolumeKey = "TowerDefense.EffectVolume";
    private Texture2D rankShield, rankCrown;
    private Vector2 promotionPosition;
    private float promotionAt = -1f;
    private int promotedRank;
    private Shot frostHighlight;
    private float frostHighlightUntil;
    private int bestFrostVolley;
    private Texture2D white;
    private Texture2D[] towerIdle, towerAttack, enemyIdle;
    private Texture2D crystal, tree, flowers, bush, berryBush, log, pine, rock, stump;
    private const string Cartoon = "Cartoon/";
    private static Rect[] AnimationFrames = CreateAnimationFrames();
    private Color ink=new Color(.08f,.13f,.19f);
    private Color cyan=new Color(.32f,.88f,.88f);
    private Color gold=new Color(1f,.73f,.31f);
    private Color red=new Color(1f,.34f,.38f);
    private GUIStyle normal, heading, small, button;
    private static string[] TowerNames={"연사탑","포격탑","감속탑","저격탑","전격탑","냉각포","레일건"};
    private static string[] RankNames={"초급","중급","고급"};
    private static string[] EnemyNames={"슬라임","정찰병","중장갑병"};

    private void Awake()
    {
        Application.targetFrameRate=60;
        title=SceneManager.GetActiveScene().name=="Title";
        run = new RunController();
        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, .40f));
        effectVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectVolumeKey, .65f));
        InitializeMusic();
        font=Resources.Load<Font>("Korean");
        if(font==null) font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        white=Texture2D.whiteTexture;
        towerIdle=LoadCartoon("knight_idle","mushroom_idle","wizard_idle","ghost_idle","zombie_idle","mushroom_idle","ghost_idle");
        towerAttack=LoadCartoon("knight_attack","mushroom_attack","wizard_idle","ghost_attack","zombie_attack","mushroom_attack","ghost_attack");
        if(!title)
        {
            frostImpact = Resources.Load<AudioClip>("FrostImpact");
            promotionChime = Resources.Load<AudioClip>("PromotionChime");
            impactAudio = gameObject.AddComponent<AudioSource>();
            impactAudio.playOnAwake = false;
            impactAudio.spatialBlend = 0f;
            impactAudio.volume = effectVolume;
            rankShield = Resources.Load<Texture2D>(Cartoon+"rank_shield");
            rankCrown = Resources.Load<Texture2D>(Cartoon+"rank_crown");
            enemyIdle=LoadCartoon("slime_green_idle","goblin_idle","skeleton_idle");
            crystal=Resources.Load<Texture2D>(Cartoon+"snow_crystal");
            tree=Resources.Load<Texture2D>(Cartoon+"meadow_tree_a");
            flowers=Resources.Load<Texture2D>(Cartoon+"meadow_flowers");
            bush=Resources.Load<Texture2D>(Cartoon+"meadow_bush");
            berryBush=Resources.Load<Texture2D>(Cartoon+"meadow_berry_bush");
            log=Resources.Load<Texture2D>(Cartoon+"meadow_log");
            pine=Resources.Load<Texture2D>(Cartoon+"meadow_pine");
            rock=Resources.Load<Texture2D>(Cartoon+"meadow_rock");
            stump=Resources.Load<Texture2D>(Cartoon+"meadow_stump");
        }
    }

    private void InitializeMusic()
    {
        // The generated scenes have a camera but no audio listener.
        if (FindObjectOfType<AudioListener>() == null)
            gameObject.AddComponent<AudioListener>();

        if (calmMusic == null)
        {
            var musicPlayer = new GameObject("Title and Preparation Music");
            calmMusic = CreateMusicSource(musicPlayer, "Music/Lily-Paddling Down the Stream", musicVolume);
            if (calmMusic != null)
                DontDestroyOnLoad(musicPlayer);
            else
                Destroy(musicPlayer);
        }
        if (!title)
            battleMusic = CreateMusicSource(gameObject, "Music/empacotatron_loop", musicVolume * .9f);
        UpdateMusic();
    }

    private static AudioSource CreateMusicSource(GameObject owner, string resourcePath, float volume)
    {
        AudioClip clip = Resources.Load<AudioClip>(resourcePath);
        if (clip == null) return null;

        AudioSource music = owner.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.clip = clip;
        music.spatialBlend = 0f;
        music.loop = true;
        music.volume = volume;
        return music;
    }

    private void UpdateMusic()
    {
        AudioSource desired = run.Phase == RunPhase.Fighting && battleMusic != null ? battleMusic : calmMusic;
        if (desired == activeMusic) return;

        if (activeMusic != null) activeMusic.Pause();
        activeMusic = desired;
        if (activeMusic == null || activeMusic.isPlaying) return;

        if (activeMusic.timeSamples > 0) activeMusic.UnPause();
        else activeMusic.Play();
    }

    private static Rect[] CreateAnimationFrames()
    {
        Rect[] frames = new Rect[16];
        for (int index = 0; index < frames.Length; index++)
        {
            int column = index % 4;
            int row = 3 - index / 4;
            frames[index] = new Rect(column / 4f, row / 4f, .25f, .25f);
        }
        return frames;
    }

    private static Texture2D[] LoadCartoon(params string[] names)
    {
        Texture2D[] textures = new Texture2D[names.Length];
        for (int index = 0; index < names.Length; index++)
            textures[index] = Resources.Load<Texture2D>(Cartoon + names[index]);
        return textures;
    }

    private void Update()
    {
        UpdateMusic();
        if (title || run.Phase != RunPhase.Fighting)
        {
            accumulated = 0;
            return;
        }

        double secondsPerTick = 1.0 / Rules.Hz;
        int previousCoreHp = run.CoreHp;
        Shot strongestFrostShot = null;
        accumulated += Time.deltaTime;
        while (accumulated >= secondsPerTick)
        {
            run.StepCombat();
            foreach (Shot shot in run.Combat.Shots)
            {
                if (shot.Tick == run.Combat.Tick && shot.Kills > 1
                    && run.Towers[shot.Slot].Type == TowerType.FrostCannon
                    && (strongestFrostShot == null || shot.Kills > strongestFrostShot.Kills))
                    strongestFrostShot = shot;
            }
            accumulated -= secondsPerTick;
            if (run.Phase != RunPhase.Fighting)
            {
                if (run.LastChallengeWon && promotionChime != null)
                    impactAudio.PlayOneShot(promotionChime);
                accumulated = 0;
                break;
            }
        }
        if (strongestFrostShot != null)
        {
            bestFrostVolley = Mathf.Max(bestFrostVolley, strongestFrostShot.Kills);
            if (frostHighlight == null || Time.unscaledTime >= frostHighlightUntil || strongestFrostShot.Kills >= frostHighlight.Kills)
            {
                bool playImpact = Time.unscaledTime >= frostHighlightUntil;
                frostHighlight = strongestFrostShot;
                frostHighlightUntil = Time.unscaledTime + .8f;
                if (playImpact && frostImpact != null) impactAudio.PlayOneShot(frostImpact);
            }
        }
        if (run.CoreHp < previousCoreHp) coreHitUntil = Time.unscaledTime + .25f;
    }

    private void Style()
    {
        normal = new GUIStyle(GUI.skin.label);
        normal.font = font;
        normal.fontSize = 17;
        normal.wordWrap = true;
        normal.normal.textColor = new Color(.88f,.93f,.96f);

        small = new GUIStyle(normal);
        small.fontSize = 13;

        heading = new GUIStyle(normal);
        heading.fontSize = 30;
        heading.fontStyle = FontStyle.Bold;

        button = new GUIStyle(GUI.skin.button);
        button.font = font;
        button.fontSize = 16;
        button.wordWrap = true;
    }
    private void OnGUI()
    {
        GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(Screen.width/1440f,Screen.height/900f,1));
        Style();
        Fill(new Rect(0,0,1440,900),ink);
        if(title){DrawTitle();return;}
        Label(new Rect(40,24,660,45),"타워디펜스",heading);
        DrawSoundControls(245,22);
        Label(new Rect(720,30,680,35),$"코어 {run.CoreHp} / {Rules.CoreMaxHp}     크레딧 {run.Credits}     웨이브 {run.Wave} / {Rules.FinalWave}",normal);
        DrawMap();
        Fill(new Rect(1000,90,405,748),new Color(.11f,.18f,.25f));
        if(run.Phase==RunPhase.Finished){DrawResult();return;}
        DrawForecast();
        if(run.Phase==RunPhase.Preparing) DrawPreparation();
        else
        {
            Label(new Rect(1025,390,350,40),"방어 진행 중",heading);
            Label(new Rect(1025,445,350,80),"타워가 자동으로 공격합니다.\n다음 공격에 대비해 방어 결과를 확인하세요.",normal);
        }
        if (run.Phase == RunPhase.Preparing && run.Combat != null)
            DrawPreviousWave();
        else
            Label(new Rect(45,840,930,30),run.Phase==RunPhase.Preparing?"포탑을 빈 슬롯으로 드래그해 이동 · 클릭해서 합성 또는 판매":"북쪽과 남쪽에서 코어로 진입하는 적을 막으세요.",small);
        if(GUI.Button(new Rect(1025,785,350,32),showDirector?"AI 판단 닫기":"AI 판단 보기",button)) showDirector=!showDirector;
        if(showDirector) DrawDirector();
    }
    private void DrawTitle()
    {
        for(int i=0;i<Rules.BuildCost.Length;i++)
            DrawCharacter(towerIdle[i],new Vector2(910+i%3*146,515+i/3*132),156,IdleFrame(i*3));
        GUIStyle titleStyle = new GUIStyle(heading);
        titleStyle.fontSize = 64;
        Label(new Rect(110,180,1000,80),"타워디펜스",titleStyle);

        GUIStyle descriptionStyle = new GUIStyle(normal);
        descriptionStyle.fontSize = 23;
        Label(new Rect(115,275,720,85),$"두 진입로에서 몰려오는 적을 막고 코어를 {Rules.FinalWave}웨이브 동안 지키세요.",descriptionStyle);
        Label(new Rect(115,405,720,130),"뽑기 버튼을 누르면 포탑 종류와 배치할 빈 슬롯이 무작위로 정해집니다.\n같은 종류와 등급의 포탑 3개를 합쳐 중급과 고급으로 올리세요.",normal);
        if(GUI.Button(new Rect(115,610,300,64),"방어 시작",button))SceneManager.LoadScene("Battle");
        DrawSoundControls(115,694);
        Label(new Rect(115,780,740,60),"Music · Lily-Paddling Down the Stream — Fablefly Music (CC BY 4.0)\nEmpacotatron — Fupi (CC0)",small);
    }

    private void DrawSoundControls(float x, float y)
    {
        Label(new Rect(x,y,60,24),$"음악 {Mathf.RoundToInt(musicVolume*100)}%",small);
        float music = GUI.HorizontalSlider(new Rect(x+62,y+4,112,20),musicVolume,0f,1f);
        Label(new Rect(x+198,y,77,24),$"효과음 {Mathf.RoundToInt(effectVolume*100)}%",small);
        float effects = GUI.HorizontalSlider(new Rect(x+276,y+4,112,20),effectVolume,0f,1f);
        if (music != musicVolume)
        {
            musicVolume = music;
            if (calmMusic != null) calmMusic.volume = musicVolume;
            if (battleMusic != null) battleMusic.volume = musicVolume * .9f;
            PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume);
            soundSettingsChanged = true;
        }
        if (effects != effectVolume)
        {
            effectVolume = effects;
            if (impactAudio != null) impactAudio.volume = effectVolume;
            PlayerPrefs.SetFloat(EffectVolumeKey, effectVolume);
            soundSettingsChanged = true;
        }
    }

    private void OnDisable()
    {
        if (!soundSettingsChanged) return;
        PlayerPrefs.Save();
        soundSettingsChanged = false;
    }
    private Vector2 Map(Point p)=>new Vector2(85+p.X*58,752-p.Y*54);

    private int SlotAt(Vector2 pointer, bool includeTowerSprite)
    {
        if (showDirector && new Rect(60,110,900,320).Contains(pointer)) return -1;

        for (int slot = 0; slot < run.Towers.Length; slot++)
        {
            Vector2 center = Map(Rules.Slots[slot]);
            if (new Rect(center.x-24,center.y-24,48,48).Contains(pointer)) return slot;
        }

        if (includeTowerSprite)
        {
            for (int slot = run.Towers.Length - 1; slot >= 0; slot--)
            {
                TowerPlacement tower = run.Towers[slot];
                if (tower == null) continue;
                Vector2 center = Map(Rules.Slots[slot]);
                float size = TowerSize(tower.Rank);
                if (new Rect(center.x-28,center.y+21-size,56,size).Contains(pointer)) return slot;
            }
        }
        return -1;
    }

    private void HandleSlotInput()
    {
        int control = GUIUtility.GetControlID(FocusType.Passive);
        Event input = Event.current;
        if (run.Phase != RunPhase.Preparing) return;

        EventType eventType = input.GetTypeForControl(control);
        if (eventType == EventType.MouseDown && input.button == 0 && GUIUtility.hotControl == 0)
        {
            int slot = SlotAt(input.mousePosition, includeTowerSprite: true);
            if (slot < 0) return;
            pressedSlot = slot;
            pressPosition = input.mousePosition;
            GUIUtility.hotControl = control;
            input.Use();
            return;
        }

        if (GUIUtility.hotControl != control) return;
        if (eventType == EventType.MouseDrag)
        {
            if (!draggingTower && run.Towers[pressedSlot] != null
                && (input.mousePosition - pressPosition).sqrMagnitude >= 36f)
            {
                draggingTower = true;
                selected = pressedSlot;
                moveFrom = -1;
            }
            input.Use();
        }
        else if (eventType == EventType.MouseUp && input.button == 0)
        {
            int slot = SlotAt(input.mousePosition, includeTowerSprite: !draggingTower);
            if (draggingTower)
            {
                message = slot == pressedSlot ? "이동을 취소했습니다." : TryMoveTo(pressedSlot, slot);
            }
            else if (slot == pressedSlot)
            {
                if (moveFrom >= 0) message = TryMoveTo(moveFrom, slot);
                else selected = slot;
            }
            EndSlotInput();
            input.Use();
        }
        else if (eventType == EventType.KeyDown && input.keyCode == KeyCode.Escape)
        {
            EndSlotInput();
            input.Use();
        }
        else if (eventType == EventType.Ignore)
        {
            EndSlotInput();
        }
    }

    private void EndSlotInput()
    {
        GUIUtility.hotControl = 0;
        pressedSlot = -1;
        draggingTower = false;
    }

    private string TryMoveTo(int from, int to)
    {
        string error = run.TryMove(from,to);
        if (error != null) return error;
        moveFrom = -1;
        selected = to;
        return "포탑을 옮겼습니다.";
    }

    private void DrawMap()
    {
        HandleSlotInput();
        DrawMeadow();
        Color trailEdge=new Color(.59f,.42f,.23f);
        Color trail=new Color(.85f,.68f,.43f);
        DrawTrail(Rules.North,trailEdge,44);
        DrawTrail(Rules.South,trailEdge,44);
        DrawTrail(Rules.North,trail,36);
        DrawTrail(Rules.South,trail,36);
        DrawLaneGuide(Rules.North,cyan);
        DrawLaneGuide(Rules.South,gold);
        foreach(Lane lane in new[]{Lane.North,Lane.South})
        {
            Vector2 p=Map(Rules.Path(lane).Points[0]);
            Label(new Rect(p.x-25,p.y-45,130,25),lane==Lane.North?"북쪽 입구":"남쪽 입구",small);
        }
        Vector2 core=Map(new Point(14,5));
        DrawProp(crystal,new Rect(core.x-30,core.y-48,60,60));
        Fill(new Rect(core.x-26,core.y+16,52,5),ink);
        Fill(new Rect(core.x-26,core.y+16,52f*run.CoreHp/Rules.CoreMaxHp,5),
            Time.unscaledTime < coreHitUntil ? red : cyan);
        Label(new Rect(core.x-30,core.y+32,90,30),"코어",small);
        if(selected>=0)
        {
            TowerPlacement t=run.Towers[selected];
            DrawCircle(Map(Rules.Slots[selected]),Rules.Range[t==null?0:(int)t.Type]*56,new Color(.5f,.8f,.9f,.35f));
        }
        Vector2 pointer = Event.current.mousePosition;
        int hoverSlot = draggingTower ? SlotAt(pointer, includeTowerSprite: false) : -1;
        for(int i=0;i<run.Towers.Length;i++)
        {
            Vector2 p=Map(Rules.Slots[i]);
            TowerPlacement t=run.Towers[i];
            Color slotColor = new Color(.34f,.27f,.18f);
            Color weaponColor = WeaponColor(t?.Type);
            if (weaponColor != Color.clear) slotColor = Color.Lerp(slotColor, weaponColor, .55f);
            if (i == hoverSlot && t == null) slotColor = cyan;
            Fill(new Rect(p.x-24,p.y-24,48,48),i==selected?new Color(.68f,.48f,.23f):slotColor);
            if(t!=null)
            {
                if (draggingTower && i == pressedSlot) continue;
                int type=(int)t.Type;
                Shot shot=run.Phase==RunPhase.Fighting && type<towerAttack.Length
                    ? FindLatestShot(i) : null;
                if(shot!=null)
                    DrawRankedTower(t,towerAttack[type],p+new Vector2(0,21),
                        Mathf.Clamp((run.Combat.Tick-shot.Tick)*16/13,0,15),Map(shot.Target).x<p.x);
                else
                    DrawRankedTower(t,towerIdle[type],p+new Vector2(0,21),IdleFrame(i*3));
                Label(new Rect(p.x-31,p.y+23,76,24),RankNames[t.Rank-1]+" "+TowerNames[(int)t.Type],small);
            }
            else Label(new Rect(p.x-8,p.y-12,30,25),(i+1).ToString(),small);
        }
        if (draggingTower && new Rect(45,95,920,720).Contains(pointer))
        {
            var tower = run.Towers[pressedSlot];
            DrawRankedTower(tower,towerIdle[(int)tower.Type],pointer+new Vector2(0,21),IdleFrame(pressedSlot*3));
        }
        if(run.Combat!=null && run.Phase==RunPhase.Fighting)
        {
            foreach(Shot shot in run.Combat.Shots)
            {
                Color weaponColor = WeaponColor(run.Towers[shot.Slot].Type);
                Line(Map(Rules.Slots[shot.Slot]),Map(shot.Target),
                    weaponColor == Color.clear ? new Color(1,.95f,.7f,.8f) : weaponColor,
                    weaponColor == Color.clear ? 2 : 4);
            }
            foreach(EnemyRuntime enemy in run.Combat.Enemies)
            {
                Vector2 p=Map(enemy.Position);
                Color color=enemy.Type==EnemyType.Heavy?gold:enemy.Type==EnemyType.Scout?cyan:red;
                float size=enemy.Type==EnemyType.Heavy?50:38;
                if (enemy.IsSlowed)
                    DrawCircle(p+new Vector2(0,7), size*.38f, cyan);
                DrawCharacter(enemyIdle[(int)enemy.Type],p+new Vector2(0,12),size,IdleFrame((int)(enemy.Progress*10)));
                float healthY=p.y+9-size;
                Fill(new Rect(p.x-15,healthY,30,3),new Color(.22f,.17f,.12f));
                Fill(new Rect(p.x-15,healthY,30*(float)enemy.Hp/enemy.MaxHp,3),color);
            }
            foreach(Shot shot in run.Combat.Shots)
            {
                TowerType type=run.Towers[shot.Slot].Type;
                float radius=Rules.SplashRadius[(int)type];
                if(radius==0) continue;
                Vector2 impact=Map(shot.Target);
                float progress=(run.Combat.Tick-shot.Tick)/12f;
                Color blast=type==TowerType.Spark || type==TowerType.FrostCannon?cyan:gold;
                blast.a=1f-progress*.7f;
                DrawCircle(impact,Mathf.Lerp(12,radius*56,progress),blast);
                if(shot.Kills>1 && type!=TowerType.FrostCannon)
                    Label(new Rect(impact.x-32,impact.y-52-progress*10,100,26),$"{shot.Kills}마리 처치!",small);
            }
        }
        if (frostHighlight != null && Time.unscaledTime < frostHighlightUntil)
        {
            Vector2 impact = Map(frostHighlight.Target);
            var banner = new Rect(Mathf.Clamp(impact.x-110,50,744), impact.y-65,218,30);
            Fill(banner,ink);
            Label(banner,$" 냉각포 · 한 발에 {frostHighlight.Kills}마리",normal);
        }
        DrawPromotionEffect();
    }

    private static float TowerSize(int rank) => 70f + 8f * (rank - 1);

    private void DrawRankedTower(TowerPlacement tower, Texture2D atlas, Vector2 feet, int frame, bool flip = false)
    {
        float size = TowerSize(tower.Rank);
        Color originalColor = GUI.color;
        if (tower.Rank > 1)
            GUI.color = tower.Rank == 2 ? new Color(.95f,1f,1f) : new Color(1f,.91f,.71f);
        DrawCharacter(atlas,feet,size,frame,flip);
        GUI.color = originalColor;

        if (tower.Rank > 1)
        {
            float shieldX = feet.x + (flip ? -33f : 11f);
            DrawProp(rankShield,new Rect(shieldX,feet.y-size*.62f,23,25));
        }
        if (tower.Rank > 2)
            DrawProp(rankCrown,new Rect(feet.x-12,feet.y-size+7,24,24));
    }

    private void ShowPromotion(int slot, int rank)
    {
        promotionPosition = Map(Rules.Slots[slot]);
        promotionAt = Time.unscaledTime;
        promotedRank = rank;
        if (promotionChime != null) impactAudio.PlayOneShot(promotionChime,.8f);
    }

    private void DrawPromotionEffect()
    {
        float progress = (Time.unscaledTime - promotionAt) / .85f;
        if (promotionAt < 0f || progress >= 1f) return;

        Color flash = gold;
        flash.a = 1f - progress;
        DrawCircle(promotionPosition,Mathf.Lerp(20f,62f,progress),flash);
        DrawCircle(promotionPosition,Mathf.Lerp(14f,45f,progress),flash);
        for (int ray = 0; ray < 8; ray++)
        {
            float angle = ray * Mathf.PI / 4f;
            Vector2 direction = new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
            float distance = Mathf.Lerp(25f,59f,progress);
            Line(promotionPosition+direction*distance,promotionPosition+direction*(distance+9f),flash,2);
        }
        var banner = new Rect(promotionPosition.x-53,promotionPosition.y-61-progress*18,106,29);
        Fill(banner,new Color(ink.r,ink.g,ink.b,flash.a));
        GUI.color = flash;
        Label(banner,$"  {RankNames[promotedRank-1]} 승급",normal);
        GUI.color = Color.white;
    }

    private void DrawPreviousWave()
    {
        int[] leaks = run.Combat.Leaks;
        Fill(new Rect(45,833,925,48),ink);
        Label(new Rect(57,839,900,34),
            $"{run.Wave-1}웨이브 결과 · 북쪽 돌파 {leaks[0]} · 남쪽 돌파 {leaks[1]} · 코어 피해 {run.Combat.Damage}"
            + (bestFrostVolley > 1 ? $" · 냉각포 한 발 최대 {bestFrostVolley}마리" : ""),normal);
        if (run.LastChallenge != ChallengeType.None)
        {
            string reward = run.LastChallenge == ChallengeType.Crossfire
                ? $"크레딧 +{Rules.ChallengeCreditReward}" : "즉시 승급권 획득";
            GUI.color = run.LastChallengeWon ? gold : red;
            Label(new Rect(57,868,900,24),run.LastChallengeWon ? $"도전 성공 · {reward}" : "도전 실패 · 목표 경로에서 적이 돌파했습니다.",normal);
            GUI.color = Color.white;
        }
    }
    private Shot FindLatestShot(int slot)
    {
        for (int index = run.Combat.Shots.Count - 1; index >= 0; index--)
        {
            Shot shot = run.Combat.Shots[index];
            if (shot.Slot == slot)
                return shot;
        }
        return null;
    }

    private void DrawMeadow()
    {
        Fill(new Rect(40,90,930,730),new Color(.33f,.40f,.22f));
        Fill(new Rect(45,95,920,720),new Color(.56f,.64f,.33f));

        // Keep the forest clusters in the clearings and away from tower labels.
        DrawProp(pine,new Rect(48,104,104,126));
        DrawProp(tree,new Rect(104,114,133,142));
        DrawProp(berryBush,new Rect(191,209,76,48));
        DrawProp(flowers,new Rect(271,220,45,29));
        DrawProp(log,new Rect(369,166,92,60));
        DrawProp(bush,new Rect(448,212,64,41));
        DrawProp(pine,new Rect(780,103,115,145));
        DrawProp(tree,new Rect(847,119,111,137));
        DrawProp(rock,new Rect(852,242,75,42));

        DrawProp(rock,new Rect(91,433,73,53));
        DrawProp(flowers,new Rect(166,466,49,31));
        DrawProp(stump,new Rect(338,460,78,70));
        DrawProp(berryBush,new Rect(414,491,65,39));
        DrawProp(flowers,new Rect(555,452,46,29));
        DrawProp(bush,new Rect(921,525,40,36));

        DrawProp(pine,new Rect(53,678,90,132));
        DrawProp(tree,new Rect(109,679,113,131));
        DrawProp(bush,new Rect(203,742,65,50));
        DrawProp(pine,new Rect(769,666,110,142));
        DrawProp(tree,new Rect(842,673,112,133));
        DrawProp(flowers,new Rect(726,751,51,35));
    }

    private void DrawTrail(Route route,Color color,float width)
    {
        foreach(Point point in route.Points)
        {
            Vector2 p=Map(point);
            Fill(new Rect(p.x-width/2,p.y-width/2,width,width),color);
        }
        for(int i=1;i<route.Points.Length;i++)
            Line(Map(route.Points[i-1]),Map(route.Points[i]),color,width);
    }

    private void DrawLaneGuide(Route route,Color color)
    {
        // The road from the merge to the core belongs to both lanes.
        Color guide=new Color(color.r,color.g,color.b,.85f);
        for(int i=1;i<route.Points.Length-1;i++)
        {
            Vector2 start=Map(route.Points[i-1]), end=Map(route.Points[i]);
            float length=Vector2.Distance(start,end);
            Vector2 direction=(end-start)/length;
            for(float distance=5;distance<length-3;distance+=21)
                Line(start+direction*distance,start+direction*Mathf.Min(distance+9,length),guide,3);
        }
    }
    private static int IdleFrame(int offset) => (Mathf.FloorToInt(Time.unscaledTime*12)+offset)%16;

    private static void DrawCharacter(Texture2D atlas,Vector2 feet,float size,int frame,bool flip=false)
    {
        if(atlas==null) return;
        Rect uv=AnimationFrames[frame];
        if(flip){uv.x+=uv.width;uv.width=-uv.width;}
        GUI.DrawTextureWithTexCoords(new Rect(feet.x-size/2,feet.y-size,size,size),atlas,uv);
    }

    private static void DrawProp(Texture2D texture,Rect rect)
    {
        if(texture!=null) GUI.DrawTexture(rect,texture,ScaleMode.ScaleToFit,true);
    }
    private void DrawForecast()
    {
        GUIStyle forecastStyle = new GUIStyle(heading);
        forecastStyle.fontSize = 25;
        string forecastTitle = $"{run.Wave}웨이브 공격 예고";
        switch (run.LockedPlan.Assault)
        {
            case AssaultType.Swarm: forecastTitle = $"{run.Wave}웨이브 · 군집 공세"; break;
            case AssaultType.Armored: forecastTitle = $"{run.Wave}웨이브 · 중장갑 공세"; break;
        }
        if (run.Wave == Rules.FinalWave) forecastTitle = "최종 공세";
        Label(new Rect(1025,112,350,40),forecastTitle,forecastStyle);
        int y=168;
        foreach(Lane lane in new[]{Lane.North,Lane.South})
        {
            string laneName = lane == Lane.North ? "북쪽" : "남쪽";
            if (run.LockedPlan.Primary == lane)
                laneName += " · 주력";
            if (run.LockedPlan.Challenge != ChallengeType.None && EnemyDirector.ChallengeLane(run.LockedPlan) == lane)
                laneName += " · 도전 증원";
            Label(new Rect(1025,y,350,28),laneName,normal);

            int[] enemyCounts = new int[EnemyNames.Length];
            int laneCount = 0;
            int firstHeavyPosition = 0;
            string firstEnemyName = "";
            foreach (SpawnEntry spawn in run.LockedPlan.Spawns)
            {
                if (spawn.Lane != lane) continue;

                laneCount++;
                enemyCounts[(int)spawn.Type]++;
                if (laneCount == 1)
                    firstEnemyName = EnemyNames[(int)spawn.Type];
                if (firstHeavyPosition == 0 && spawn.Type == EnemyType.Heavy)
                    firstHeavyPosition = laneCount;
            }

            string roster = "";
            for (int type = 0; type < enemyCounts.Length; type++)
            {
                if (enemyCounts[type] == 0) continue;
                if (roster.Length > 0)
                    roster += " / ";
                roster += $"{EnemyNames[type]} {enemyCounts[type]}명";
            }
            Label(new Rect(1025,y+30,350,45),roster,small);

            string detail = "첫 출현 " + firstEnemyName;
            if (run.Wave == 1)
                detail = lane == Lane.North ? "슬라임 군집 · 범위 포격 유리" : "빠른 정찰병 · 장거리 공격 유리";
            else if (lane == run.LockedPlan.Primary && run.LockedPlan.Assault == AssaultType.Swarm)
                detail = "슬라임 떼가 밀집해 진입 · 범위 공격 유리";
            else if (lane == run.LockedPlan.Primary && run.LockedPlan.Assault == AssaultType.Armored)
                detail = "강화 중장갑이 선두 · 장거리 집중 공격";
            else if (firstHeavyPosition > 0)
                detail += $" · 중장갑 {firstHeavyPosition}번째";
            detail += $"\n돌파 피해 · 슬라임 {Rules.SlimeCoreDamage} / 정찰병 {Rules.CoreDamage[(int)EnemyType.Scout]} / 중장갑 {Rules.CoreDamage[(int)EnemyType.Heavy]}";
            Label(new Rect(1025,y+61,350,43),detail,small);
            y+=110;
        }
    }
    private void DrawPreparation()
    {
        Label(new Rect(1025,390,350,32),"준비",normal);
        if (GUI.Button(new Rect(1025,427,350,46), $"포탑 뽑기 · {Rules.DrawCost} 크레딧", button))
        {
            string error = run.TryDrawTower(out int placedSlot);
            if (error == null)
            {
                selected = placedSlot;
                moveFrom = -1;
                TowerPlacement tower = run.Towers[placedSlot];
                message = $"초급 {TowerNames[(int)tower.Type]} · 슬롯 {placedSlot + 1}에 배치";
                if (run.Wave == 1 && run.TryGetMissingIngredient(placedSlot, out TowerType missing, out TowerType weapon))
                    message += $"\n{TowerNames[(int)missing]}을 얻으면 {TowerNames[(int)weapon]} 조합";
            }
            else
            {
                message = error;
            }
        }

        TowerPlacement t = selected < 0 ? null : run.Towers[selected];
        if (t == null)
        {
            Label(new Rect(1025,489,350,62),"포탑은 무작위 빈 슬롯에 바로 배치됩니다.\n한 묶음에 각 종류가 한 번씩 나옵니다.",small);
        }
        else
        {
            Label(new Rect(1025,481,350,30),$"슬롯 {selected+1} · {RankNames[t.Rank-1]} {TowerNames[(int)t.Type]}",normal);
            TowerType ingredient = t.Type;
            TowerType result = t.Type;
            bool hasRecipe = t.Rank == 1 && run.TryGetRecipe(t.Type, out ingredient, out result);
            if(t.Rank<Rules.MaxRank && GUI.Button(new Rect(1025,517,hasRecipe?170:350,38),$"{RankNames[t.Rank]} 합성 · {run.MatchingTowers(t)} / {Rules.MergeCount}",button))
            {
                string error = run.TryUpgrade(selected);
                if (error == null) ShowPromotion(selected,t.Rank);
                message = error ?? $"{RankNames[t.Rank-1]} {TowerNames[(int)t.Type]} 완성";
            }
            if (hasRecipe && t.Rank == 1)
            {
                int material = run.FindRecipeIngredient(ingredient);
                string recipe = material >= 0
                    ? $"{TowerNames[(int)result]} 조합 · {material + 1}번"
                    : $"{TowerNames[(int)result]} · {TowerNames[(int)ingredient]} 필요";
                if (GUI.Button(new Rect(1205,517,170,38), recipe, button))
                {
                    string error = run.TryCombine(selected);
                    if (error == null) moveFrom = -1;
                    message = error ?? (result == TowerType.FrostCannon
                        ? "냉각포 완성 · 범위 포격과 감속"
                        : "레일건 완성 · 장거리 속사");
                }
            }
            if (GUI.Button(new Rect(1025,563,170,38), moveFrom >= 0 ? "이동 취소" : "이동", button))
            {
                moveFrom = moveFrom >= 0 ? -1 : selected;
                message = moveFrom >= 0 ? "옮길 빈 슬롯을 클릭하세요." : "이동을 취소했습니다.";
            }
            if(GUI.Button(new Rect(1205,563,170,38),$"판매 +{t.Invested/2}",button))
            { message=run.TrySell(selected)??"판매했습니다.";moveFrom=-1; }
        }
        if (run.HasOpeningSupply)
            DrawOpeningSupply();
        else if (run.HasSupplyReward)
            DrawSupplyReward();
        Label(new Rect(1025,672,350,42),message,small);
        DrawChallengeControls();
        if(GUI.Button(new Rect(1025,720,350,48),"전투 시작",button))
        {
            run.TryStartCombat();
            frostHighlight = null;
            frostHighlightUntil = 0f;
            bestFrostVolley = 0;
            selected = -1;
            moveFrom = -1;
            message = "";
            accumulated = 0;
        }
    }

    private void DrawChallengeControls()
    {
        if (run.HasChallengePromotion)
        {
            Label(new Rect(1025,825,350,23),"다음 전투 전까지 · 초급 또는 중급 포탑 선택",small);
            if (GUI.Button(new Rect(1025,850,350,34),"도전 보상 · 선택한 포탑 즉시 승급",button))
            {
                string error = run.TryUseChallengePromotion(selected);
                if (error == null) ShowPromotion(selected,run.Towers[selected].Rank);
                message = error ?? "도전 보상으로 포탑을 승급했습니다.";
            }
            return;
        }
        if (!run.CanChooseChallenge) return;

        ChallengeType active = run.LockedPlan.Challenge;
        string primary = run.LockedPlan.Primary == Lane.North ? "북쪽" : "남쪽";
        string secondary = run.LockedPlan.Primary == Lane.North ? "남쪽" : "북쪽";
        string hint = active == ChallengeType.None ? "도전 선택 · 증원 경로를 돌파 0명으로 막으세요."
            : $"{(active == ChallengeType.Crossfire ? secondary : primary)} 증원 선택됨 · 다시 누르면 취소";
        Label(new Rect(1025,825,350,22),hint,small);
        if (GUI.Button(new Rect(1025,850,173,34),$"{secondary} 군집 · +{Rules.ChallengeCreditReward}",button))
            message = run.TryChooseChallenge(active == ChallengeType.Crossfire ? ChallengeType.None : ChallengeType.Crossfire) ?? "";
        if (GUI.Button(new Rect(1204,850,171,34),$"{primary} 중장갑 · 승급권",button))
            message = run.TryChooseChallenge(active == ChallengeType.Vanguard ? ChallengeType.None : ChallengeType.Vanguard) ?? "";
    }

    private void DrawOpeningSupply()
    {
        Label(new Rect(1025,607,350,24),"첫 보급 · 재료 또는 크레딧",normal);
        int recipeSlot = selected;
        if (!run.TryGetMissingIngredient(recipeSlot, out TowerType ingredient, out TowerType weapon))
        {
            recipeSlot = -1;
            for (int slot = 0; slot < run.Towers.Length; slot++)
            {
                if (!run.TryGetMissingIngredient(slot, out ingredient, out weapon)) continue;
                recipeSlot = slot;
                break;
            }
        }

        if (recipeSlot >= 0 && GUI.Button(new Rect(1025,634,227,34),
            $"{TowerNames[(int)ingredient]} 받기 · {TowerNames[(int)weapon]} 재료",button))
        {
            string error = run.TryClaimOpeningMaterial(recipeSlot);
            if (error == null)
            {
                selected = recipeSlot;
                moveFrom = -1;
            }
            message = error ?? $"재료 획득 · {TowerNames[(int)weapon]} 조합 버튼을 누르세요.";
        }
        else if (recipeSlot < 0)
        {
            Label(new Rect(1025,637,224,32),"빠진 조합 재료가 없습니다.",small);
        }
        if (GUI.Button(new Rect(1260,634,115,34),$"크레딧 +{Rules.DrawCost}",button))
            message = run.TryClaimOpeningCredits() ?? "뽑기 한 번분의 크레딧을 받았습니다.";
    }

    private static Color WeaponColor(TowerType? type)
    {
        switch (type)
        {
            case TowerType.FrostCannon: return new Color(.35f,.85f,1f);
            case TowerType.Railgun: return new Color(.86f,.58f,1f);
            default: return Color.clear;
        }
    }

    private void DrawSupplyReward()
    {
        Label(new Rect(1025,607,350,24),"지원 보급 · 하나 선택",normal);
        if (GUI.Button(new Rect(1025,634,112,34), $"크레딧 +{Rules.SupplyCredits}", button))
            message = run.TryClaimSupply(SupplyReward.Credits) ?? "추가 크레딧을 받았습니다.";
        if (GUI.Button(new Rect(1144,634,112,34), $"코어 수리 +{Rules.SupplyRepair}", button))
            message = run.TryClaimSupply(SupplyReward.Repair) ?? "코어를 수리했습니다.";
        if (GUI.Button(new Rect(1263,634,112,34), "초급 승급", button))
        {
            string error = run.TryClaimSupply(SupplyReward.Promotion,selected);
            if (error == null) ShowPromotion(selected,run.Towers[selected].Rank);
            message = error ?? "선택한 포탑이 중급으로 승급했습니다.";
        }
    }

    private void DrawResult()
    {
        Label(new Rect(1025,145,350,55),run.Won?"방어 성공":"방어 실패",heading);
        Label(new Rect(1025,225,350,180),$"도달 웨이브 {run.Wave} / {Rules.FinalWave}\n\n코어 {run.CoreHp} / {Rules.CoreMaxHp}\n남은 크레딧 {run.Credits}\n북쪽 돌파 {run.TotalLeaks[0]}명\n남쪽 돌파 {run.TotalLeaks[1]}명",normal);
        if(GUI.Button(new Rect(1025,470,350,50),"다시 도전",button))
        {run.StartNewRun();selected=-1;moveFrom=-1;showDirector=false;message="";accumulated=0;frostHighlight=null;promotionAt=-1f;}
        if(GUI.Button(new Rect(1025,535,350,50),"메인 메뉴",button)) SceneManager.LoadScene("Title");
    }
    private void DrawDirector()
    {
        Fill(new Rect(60,110,900,320),new Color(.07f,.11f,.16f,.98f));
        Label(new Rect(85,130,840,35),"적 지휘관 판단",heading);
        if(run.Decision==null){Label(new Rect(85,190,800,50),"1웨이브는 고정 편성입니다.",normal);return;}
        Label(new Rect(85,180,840,35),
            (run.LockedPlan.Challenge == ChallengeType.None ? "선택 기준" : "도전 증원 전 선택 기준")
            +" · "+run.Decision.Reason,normal);
        int y=224;
        foreach(var e in run.Decision.Evaluations)
        {
            Label(new Rect(85,y,840,28),$"{(e.Plan.Primary==Lane.North?"북쪽":"남쪽")} 주력 · 편성 {e.Plan.Template}   예상 피해 {e.Damage}   합류 후 처치 {e.MergeKills}   직전 돌파 {e.PreviousLeaks}",small);y+=34;
        }
    }
    private void Label(Rect rect,string text,GUIStyle style)=>GUI.Label(rect,text,style);
    private void Fill(Rect rect,Color color){GUI.color=color;GUI.DrawTexture(rect,white);GUI.color=Color.white;}
    private void Line(Vector2 a,Vector2 b,Color color,float width)
    {
        Matrix4x4 old=GUI.matrix;
        GUI.matrix = old * Matrix4x4.TRS(new Vector3(a.x, a.y, 0),
            Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg), Vector3.one);
        Fill(new Rect(0,-width/2,Vector2.Distance(a,b),width),color);GUI.matrix=old;
    }
    private void DrawCircle(Vector2 center,float radius,Color color)
    {
        for(int i=0;i<64;i++)
        {
            float a=i*Mathf.PI*2/64,b=(i+1)*Mathf.PI*2/64;
            Line(center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,
                center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,color,1);
        }
    }
}



