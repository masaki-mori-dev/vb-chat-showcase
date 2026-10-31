/*
 * 通知音（Sounds）。部屋・ラウンジ・トップページで共通
 *
 *、Scripts/chat-room.js の中から、ここへ外へ出した。
 * ラウンジ（ログアウトの音）とトップページ（ラウンジへ入る音）でも鳴らすことになり、
 * 同じものを写すと、音量の倍率・直前の音との間引き・オン/オフの判断が、
 * いずれ片方だけ直されて食い違うため（トグルスイッチの CSS が2か所にあるのと同じ話を、作らない）。
 *
 * 【使い方】
 *   var sounds = VbChatSounds.create({ enterUrl: '…', leaveUrl: '…', knockUrl: '…', log: function (text) {} });
 *   sounds.setEnabled(VbChatSounds.readSavedEnabled(vbChatSettings.notificationSoundDefault));
 *   sounds.setVolumeLevel(VbChatSounds.readSavedVolumeLevel(sounds, vbChatSettings.notificationSoundVolumeDefault));
 *   VbChatSounds.unlockOnFirstInput(sounds);
 * 部屋（chat-room.js）は、オン/オフと音量を自分の設定パネルと一緒に扱うので、
 * create だけを使い、残りは今までどおり自分で持っている。
 *
 * 【このファイルが読めなかったとき】
 * 部屋は「鳴らない Sounds」に落ちて、会話はそのまま続けられる（chat-room.js の Sounds を作るところ）。
 * ラウンジとトップページも、音が鳴らないだけで先へ進む。
 *
 */
(function (window) {

    'use strict';

    /*
        === 通知音 ===

        【鳴らす口は、この Sounds ひとつだけ】
        呼ぶ側が書くのは Sounds.enter() / Sounds.leave() / Sounds.message() の
        一行だけ。中身が「その場で波形を合成する」でも「音声ファイルを再生する」でも、
        呼ぶ側は変わらない。オン/オフの判断も、音量も、この中に閉じてある。
        後から気に入った音を拾ってきて差し替えるときは、この中だけを直せばよい

        【なぜ、音声ファイルではないのか】
        配信するファイルが増えない（＝サーバーへの通信が1つも増えない）、
        素材のライセンスが関係ない、「もう少し高く」「もう少し短く」が
        数字の変更だけで済む、の3つが理由

        【最大の落とし穴：過去ログでは鳴らさない】
        部屋へ入った瞬間に、過去ログの行数ぶん鳴ってはいけない。
        これは「鳴らす場所を、ハブから届いたときだけに限る」ことで、
        構造的に起きないようにしてある。過去ログの描画（renderLogEntries）は
        appendMessageRow / appendSystemMessageRow しか呼ばず、Sounds には触れない。

        【罠】音を appendRowHtml の側へ移してはいけない。
        一見「行を足すところで鳴らす」のが自然に見えるが、移した瞬間に、
        部屋へ入るたび過去ログの行数ぶん鳴るようになる
    */

    /*
        === 1つの画面ぶんの Sounds を作る ===

        options.enterUrl … 入ってくる音のファイル（無ければ合成音）
        options.leaveUrl … 出ていく音のファイル（無ければ合成音）
        options.knockUrl … ノックの音のファイル（無ければ合成音。部屋だけが渡す）
        options.log      … 動きを書き残す関数（部屋は debugLog を渡す。無ければ何もしない）

        【1つの画面で1回だけ呼ぶこと】AudioContext を中に1つ持つ（下の ctx のコメント）
    */
    function create(options) {

        options = options || {};

        var debugLog = (typeof options.log === 'function') ? options.log : function () { };


        /* 音量（0〜1）。入室・退室は少し大きめ、発言は控えめにしてある */
        var VOLUME_ENTER = 0.45;
        var VOLUME_LEAVE = 0.45;
        var VOLUME_MESSAGE = 0.35;
        var VOLUME_KNOCK = 0.45;

        /*
            利用者が選べる音量の倍率（設定パネルの「通知音の音量」）

            【端末のスピーカーの差を、ここで吸収している】
            実測で、ChromeOS機はWindows機の約2倍でちょうど良かった。
            サイト側で1つの値に決められないので、利用者に選ばせている。

            【段階の名前と倍率を、ここで結びつけている】
            Cookieとサーバーの設定が持つのは名前（low/normal/high）だけで、
            倍率はここにしかない。あとで「大」を2.5にしたくなったら、
            この表の数字を1つ直せば、既に「大」を選んでいる人にも即座に効く
            【刻み幅が上下で違うのは、意図してのこと】
              下（0.25 → 0.5 → 1.0）は ×2 刻み。小さくするぶんには制約が無いので広くとる
              上（1.0 → 1.4 → 2.0）は ×1.4 刻み。2.0 が天井（それ以上はドア音が歪む。
              FILE_BASE_GAIN の節を参照）なので、そこまでを2段に割るとこの幅になる

            【段階の名前を、3段階のときから全部付け替えてある】
            前は low(0.5) / normal(1.0) / high(2.0) だった。
            lowest/low/normal/high/highest と名付けると high の倍率が 2.0 → 1.4 に
            黙って変わり、既に選んでいた人の音が理由も分からず小さくなる。
            名前を変えたので、古い low / high は「知らない名前」になり、
            下の setVolumeLevel が既定（ふつう）へ落とす。
            normal だけは倍率が変わらないので、同じ名前を残してある
        */
        var VOLUME_SCALES = {
            'very-quiet': 0.25,
            'quiet': 0.5,
            'normal': 1.0,
            'loud': 1.4,
            'very-loud': 2.0
        };
        var DEFAULT_VOLUME_LEVEL = 'normal';

        /*
            音の輪郭（エンベロープ）の、立ち上がりにかける時間（秒）

            【罠】いきなり音量を上げ下げすると、始まりと終わりで「プツッ」と鳴る。
            短い時間をかけて動かすことで、その雑音が消える
        */
        var ATTACK_SEC = 0.008;

        /*
            ほぼ無音。0そのものは使えない
            （exponentialRampToValueAtTime は 0 を渡すと例外になる）
        */
        var SILENT = 0.0001;

        /*
            鳴らす音の並び。{ type, freq, duration } を、書いた順に続けて鳴らす

            type … 波形（'sine' は丸い音、'triangle' は少し芯のある音）
            freq … 高さ（Hz）。523＝ド、784＝ソ、880＝ラ

            入室は「ド→ソ」と上がり、退室は「ソ→ド」と下がる。
            上下が逆なので、どちらが起きたのかが、音を聞き分けなくても分かる
        */
        var TONE_ENTER = [
            { type: 'triangle', freq: 523, duration: 0.09 },
            { type: 'triangle', freq: 784, duration: 0.13 }
        ];
        var TONE_LEAVE = [
            { type: 'triangle', freq: 784, duration: 0.09 },
            { type: 'triangle', freq: 523, duration: 0.13 }
        ];
        /*
            【発言音を、sine 0.06秒 から triangle 0.08秒 に変えた】
            実機で、発言音だけ小さく聞こえた。原因は3つ重なっている——
            sineは倍音が無くていちばん痩せて聞こえる／短い音ほど人の耳は小さく感じる／
            振幅も元から低い（0.35 対 0.45）。

            【振幅を上げるのではなく、波形を変えて直している】
            振幅を上げると、ちょうど良く聞こえていた端末のほうで
            発言だけが大きくなり、入退室とのバランスが崩れる。
            倍音を足せば、音量を上げずに通りだけが良くなる
        */
        var TONE_MESSAGE = [
            { type: 'triangle', freq: 880, duration: 0.08 }
        ];
        /*
            ノック。本命は拾ってきたノックの音（knock.mp3）で、これは読めなかったときの受け皿。
            低い音を短く2回（「コン、コン」）。間は rest（音を出さずに時計だけ進める）で空ける。
            入退室（ド・ソ）とも発言（ラ）とも違う高さにして、何が起きたか聞き分けられるようにしてある
        */
        var TONE_KNOCK = [
            { type: 'triangle', freq: 330, duration: 0.06 },
            { rest: 0.07 },
            { type: 'triangle', freq: 330, duration: 0.06 }
        ];

        /* 今、音を鳴らしてよいか（設定パネルのオン/オフ） */
        var enabled = false;

        /* 今の音量の倍率（設定パネルの「通知音の音量」） */
        var volumeScale = VOLUME_SCALES[DEFAULT_VOLUME_LEVEL];

        /*
            音と音の、最短の間隔（秒）。これより近い音は鳴らさない

            【なぜ要るのか：重なると歪むため】
            Web Audioの出力は±1.0で頭打ちになるので、合計がそれを超えると
            そこで波形が潰れて「プツッ」と割れる。
            OfflineAudioContextで測った合計のピーク——

              大・入室2つがぴったり同時 … 1.75（歪む）
              大・発言2つがぴったり同時 … 1.32（歪む）
              中・入室3つがぴったり同時 … 1.31（歪む）
              入室2つが10msずれた場合  … 0.87（歪まない）

            【「大」だけの問題ではない】中でも3つ重なれば歪む。
            だから倍率の上限をどうこうするのではなく、重なりそのものを止めている。

            【なぜ「ずらす」ではなく「鳴らさない」のか】
            40ms以内に届いた2つの音は、人の耳では2つとは聞き取れない。
            遅らせて鳴らしても「1つの音」にしか聞こえないうえ、
            立て続けに届いたときに音が延々と後ろへ積み上がっていく。
            鳴らさないほうが、失うものが無い

            【罠】これは「同じ種類の音」だけの話ではない。
            入室と発言がぴったり重なっても歪むので、種類を問わず1本の時計で見る
        */
        var MIN_PLAY_INTERVAL_SEC = 0.04;

        /*
            退室の音を、最大どれだけ待つか（leaveThenRun）。

            **実測＝`door-close.mp3` は 1.013 秒**（`door-open.mp3` は 1.008 秒）。
            **最後まで待つと、押してから1秒間なにも起きない。**
            ドアの音は前半で「閉まった」と分かり、後ろは余韻なので、
            **700ms で切り上げる**ことにした。
            移動すればどのみち音は止まるので、切り上げても聞こえ方はほとんど変わらない。

            【ここは実機で決める1行である】
            「途中で切れて聞こえる」なら 1100 まで伸ばす。
            「押してから動くのが遅い」なら 500 まで詰める。
            **鳴らしてみないと決まらない、と分かっているので、
            1か所だけ触れば変えられる形にしてある**
        */
        var LEAVE_WAIT_MAX_MS = 700;

        /* 最後に音を鳴らし始めた時刻（AudioContextの時計）。まだ鳴らしていなければnull */
        var lastPlayAt = null;


        /*
            === 入室・退室は、拾ってきたドアの音（mp3）を鳴らす ===

            【合成音とまったく同じ道を通す】
            new Audio() では鳴らさない。あちらは音量が別系統になって
            上の倍率が効かないうえ、同じ音を続けて鳴らすと前の音が止まる。
            AudioContext ＋ GainNode に揃えておけば、小・中・大も、
            直前の音との間隔（MIN_PLAY_INTERVAL_SEC）も、そのまま効く

            【mp3にだけ、基準の音量を持たせている理由】
            この素材はピークとRMSの差が20dB以上ある（＝短い衝撃音＋静かな余韻）。
            体感の大きさを2倍にしようとすると、頭の衝撃だけが先に天井へぶつかる。
            実測（OfflineAudioContextで計測）——

              素材そのもの   … door-open ピーク0.392 / door-close ピーク0.554
              基準1.0・大    … 退室が単発で 1.108（＝鳴るたび毎回歪む）
              基準0.8・大    … 退室 0.886／入室3つ200msおき 0.936（歪まない）

            【なぜ「大」の倍率のほうを下げないのか】
            大が要るのは、ChromeOS機で**発言音**が聞こえなかったため。
            発言音（合成）は大でも0.7で余裕があり、歪んでいるのはドア音だけである。
            倍率を下げると、問題のない発言音まで巻き添えになり、
            2倍が必要だった当の場所が直らない。
            **天井は「設定」ではなく「その音」の性質**なので、音の側に持たせている

            【0.85ではなく0.8】0.85だと入室3つで0.994になり、余白が無くなる
        */
        var FILE_BASE_GAIN = 0.8;

        /* 効果音ファイルの場所（隠しフィールドから受け取る） */
        /*
            どこから読むかは、呼んだ画面が決める（create の options）。
            部屋はドアの音、ラウンジとトップページは自分の音（lounge-enter / lounge-leave）。
            渡されなかった音は読みに行かず、合成音で鳴る
        */
        var SOUND_FILE_URLS = {
            enter: options.enterUrl || null,
            leave: options.leaveUrl || null,
            knock: options.knockUrl || null
        };

        /* 読み込んだ音。読めていなければnullのままで、そのときは合成音が鳴る */
        var soundBuffers = { enter: null, leave: null, knock: null };

        /*
            読み込みの状態（idle / loading / ready / failed）。
            二重に読みに行かないための印でもある
        */
        var soundLoadState = { enter: 'idle', leave: 'idle', knock: 'idle' };

        /*
            効果音を読み込む

            【通知音がオンのときにしか呼ばない】
            既定はオフなので、鳴らさない人には1バイトも送らない。
            オンにした瞬間と、オンのままページを開いたときだけ読みに行く

            【読むのは1回だけ】soundLoadStateで見張っている。
            鳴らすたびに読み直したりデコードし直したりはしない
        */
        /*
            【取りに行くのと、デコードするのを分けてある】
            デコードには AudioContext が要るが、AudioContext は最初の操作まで作らない（下の ensureContext）。
            そこで、ファイルのバイト列だけは今までどおりすぐ取りに行って soundBytes に控え、
            AudioContext ができた時点（か、バイト列が届いた時点で既にあれば、その場）でデコードする。
            こうしておけば、最初の操作のあとに届いた入退室も、今までどおりドアの音で鳴る
            （状態は idle → loading → fetched → decoding → ready。どこで失敗しても failed ＝合成音）
        */
        var soundBytes = { enter: null, leave: null, knock: null };

        function decodeIfReady(key) {

            if (soundLoadState[key] !== 'fetched' || ctx === null) {
                return;
            }

            var bytes = soundBytes[key];

            soundBytes[key] = null;
            soundLoadState[key] = 'decoding';

            ctx.decodeAudioData(bytes).then(function (buffer) {

                soundBuffers[key] = buffer;
                soundLoadState[key] = 'ready';
                debugLog('効果音を読み込みました（' + key + '）');

            }).catch(function (e) {
                soundLoadState[key] = 'failed';
                debugLog('効果音を読み込めませんでした（' + key + '）。合成音で鳴らします： ' + (e && e.message));
            });
        }

        function loadSoundFile(key) {

            if (soundLoadState[key] !== 'idle') {
                return;
            }

            var url = SOUND_FILE_URLS[key];

            /*
                fetchが無い環境（かなり古いブラウザ）は、はじめから諦める。
                諦めても合成音が鳴るので、利用者から見て無音にはならない
            */
            if (!url || typeof fetch !== 'function') {
                soundLoadState[key] = 'failed';
                return;
            }

            soundLoadState[key] = 'loading';

            fetch(url).then(function (res) {

                /*
                    【罠】fetchは404でもrejectしない。res.okを自分で見ること。
                    見ないと、404のHTMLをデコードしようとして
                    分かりにくい形で落ちる
                */
                if (!res.ok) {
                    throw new Error('HTTP ' + res.status);
                }

                return res.arrayBuffer();

            }).then(function (bytes) {

                soundBytes[key] = bytes;
                soundLoadState[key] = 'fetched';

                /* AudioContext がもうあれば、その場でデコードする（無ければ、できた時点で ensureContext が呼ぶ） */
                decodeIfReady(key);

            }).catch(function (e) {

                /*
                    【失敗しても、黙って無音にはしない】
                    合成音へ落ちるので「ドアの音じゃない」で気づける。
                    鳴らさない作りにすると、置き忘れに気づく手がかりが無くなる
                */
                soundLoadState[key] = 'failed';
                debugLog('効果音を読み込めませんでした（' + key + '）。合成音で鳴らします： ' + e.message);
            });
        }

        /* 通知音がオンになったときに、まとめて読み込む */
        function preloadSoundFiles() {

            /*
                AudioContext を持たないブラウザでは、取りに行っても鳴らせないので取りに行かない。
                【ここで AudioContext を作らない】作るのは ensureContext だけで、
                最初の操作まで待つ。バイト列だけ先に取っておき、デコードはそのあと（上の decodeIfReady）
            */
            if (unsupported || !(window.AudioContext || window.webkitAudioContext)) {
                return;
            }

            loadSoundFile('enter');
            loadSoundFile('leave');

            /*
                ノックの音は、URL を渡された画面（部屋）だけが読む。
                渡されていなければ loadSoundFile が 'failed' にするだけで、取りに行かない
            */
            if (SOUND_FILE_URLS.knock) {
                loadSoundFile('knock');
            }
        }

        /*
            === AudioContext を作ってよいか===

            ページを開いた瞬間（まだ誰も触っていない）に AudioContext を作ると、Chrome がコンソールに
            「The AudioContext was not allowed to start. It must be resumed (or created) after a user gesture」を
            2行出していた（作ったときと resume を呼んだとき）。害は無い（最初にどこかを触れば動き出す）が、
            **F12 を開いて最初に見る場所に赤が出る**（開発者）。
            そこで、**このページで一度でも操作があるまで作らない**。

            【鳴るはずの場面で鳴らなくならないか】
            操作前は、作っても止められた（suspended）ままで、もともと鳴っていなかった
            （ブラウザの自動再生制限を実際に測った）。作らなくても失うものは無い。
            操作があったあとは、操作の最中でなくても作ってよい（Chrome は「このページで一度でも操作があった」＝
            sticky activation を見る）ので、SignalR で届いた入退室の音もそのまま鳴る。

            【見るもの】navigator.userActivation.hasBeenActive（そのページで一度でも操作があったか）。
            これを持たないブラウザは、今までどおりすぐ作る（警告を出すのは Chrome で、Chrome は持っている）。
            navigator.getAutoplayPolicy（今は Firefox だけ）が「許す」と答えれば、操作前でも作る
        */
        function mayStartAudio() {

            try {
                if (typeof navigator.getAutoplayPolicy === 'function' &&
                    navigator.getAutoplayPolicy('audiocontext') === 'allowed') {
                    return true;
                }
            } catch (e) {
                /* 知らない種類を渡したときに投げるブラウザがある。下の判断へ */
            }

            var ua = navigator.userActivation;

            return !ua || ua.hasBeenActive === true;
        }

        /*
            AudioContext。【1つだけ作って使い回す】
            鳴らすたびに作ると、ブラウザが持てる数の上限（数十個）にすぐ達し、
            そこから先は無言になる。しかもエラーは出ないため、原因が分かりにくい
        */
        var ctx = null;

        /* AudioContextを持たないブラウザ。一度そう分かったら、二度と試さない */
        var unsupported = false;

        /* AudioContextを用意する（無ければ作る） */
        function ensureContext() {

            if (unsupported) {
                return null;
            }

            if (ctx === null) {

                var Ctor = window.AudioContext || window.webkitAudioContext;

                if (!Ctor) {
                    unsupported = true;
                    debugLog('この環境では通知音を鳴らせません（AudioContextが無い）');
                    return null;
                }

                /* まだ誰も操作していなければ、作らない（上の mayStartAudio。鳴らないだけで、あとで作れる） */
                if (!mayStartAudio()) {
                    return null;
                }

                try {
                    ctx = new Ctor();
                } catch (e) {
                    unsupported = true;
                    debugLog('通知音の準備に失敗しました');
                    return null;
                }

                /* 先に取っておいた効果音のバイト列を、ここでデコードする */
                decodeIfReady('enter');
                decodeIfReady('leave');
                decodeIfReady('knock');
            }

            /*
                自動再生の制限で止められている場合は、動かしてみる。

                【戻り値のPromiseは待たない】待っても、今から鳴らそうとしている
                この一発には間に合わない。次に鳴らすときには効いている
            */
            if (ctx.state === 'suspended') {
                try {
                    ctx.resume();
                } catch (e) {
                    /* 止められたままでも、音が鳴らないだけ。会話は続けられる */
                }
            }

            return ctx;
        }

        /*
            1つの音を鳴らす

            spec.fileKey … 効果音ファイルの名前（'enter' / 'leave'）。無ければnull
            spec.tones   … ファイルが無い／読めなかったときに鳴らす、合成音の並び
            spec.volume  … 合成音の音量

            【ファイルと合成音の分かれ道は、ここ1か所だけ】
            呼び出し側（enter / leave / message）は、どちらで鳴るかを知らない。
            だから後から音を差し替えても、呼ぶ側は1文字も変わらない
        */
        /*
            【戻り値＝これから何秒ぶん鳴るか】
            鳴らなかったときは 0。退室の音だけは「鳴り終わってから移動する」ので、
            **呼んだ側が待つ長さを知る必要がある。**
            鳴らすかどうかの判断（オフ・操作前・近すぎる）は、すべてこの中にあるので、
            **呼んだ側は 0 かどうかだけを見ればよい**（判断を外へ持ち出さない）
        */
        function play(spec) {

            if (!enabled) {
                return 0;
            }

            var audio = ensureContext();

            if (!audio) {
                return 0;
            }

            /*
                【直前の音と近すぎるなら、鳴らさない】

                比べているのはAudioContextの時計（audio.currentTime）で、
                Date.now()ではない。音を並べているのはこちらの時計であり、
                実際に重なるかどうかもこちらで決まるため。

                【ぴったり同時は、実際に起きる】
                ハブのイベントが同じ実行の順番でまとめて届くと、
                どちらの呼び出しも同じ currentTime を読む（＝ずれ0ms）。
                2人がほぼ同時に入室した場合などが、これに当たる
            */
            var now = audio.currentTime;

            if (lastPlayAt !== null && (now - lastPlayAt) < MIN_PLAY_INTERVAL_SEC) {
                return 0;
            }

            lastPlayAt = now;

            /* ファイルが読めていれば、そちらを鳴らす */
            var buffer = spec.fileKey ? soundBuffers[spec.fileKey] : null;

            if (buffer) {

                var src = audio.createBufferSource();
                var fileGain = audio.createGain();

                src.buffer = buffer;

                /*
                    【音量の倍率が掛かるのは、ファイルでもここ1か所】
                    基準（FILE_BASE_GAIN）を先に掛けてから、小・中・大を掛ける
                */
                fileGain.gain.value = FILE_BASE_GAIN * volumeScale;

                src.connect(fileGain);
                fileGain.connect(audio.destination);

                /*
                    オシレーターと違い、stop()は要らない
                    （鳴らし終わると自分で止まり、参照も外れる）
                */
                src.start(now);

                return buffer.duration;
            }

            /* ここから下は合成音。ファイルが無い（発言）か、読めなかったとき */

            /*
                【音量の倍率を掛けるのは、ここ1か所だけ】
                呼び出し側（enter / leave / message）は、素の音量だけを渡してくる。
                掛け算をあちらへ散らすと、音を1つ足すたびに掛け忘れが起きる
            */
            var scaled = spec.volume * volumeScale;

            /*
                鳴らし始める時刻。1つ鳴らすごとに、その長さのぶん先へ進める
                （上で読んだ now をそのまま使う。読み直すと、控えた時刻と
                  実際に鳴らし始める時刻が、わずかにずれる）
            */
            var at = now;

            spec.tones.forEach(function (tone) {

                /* 休み（音を出さずに時計だけ進める。ノックの「コン、コン」の間） */
                if (tone.rest) {
                    at += tone.rest;
                    return;
                }

                var osc = audio.createOscillator();
                var gain = audio.createGain();

                osc.type = tone.type;
                osc.frequency.setValueAtTime(tone.freq, at);

                /*
                    音量を、ほぼ無音 → ピーク → ほぼ無音、と動かす。
                    立ち上がりが音そのものより長くならないよう、半分で頭打ちにしている
                */
                var peakAt = at + Math.min(ATTACK_SEC, tone.duration / 2);

                gain.gain.setValueAtTime(SILENT, at);
                gain.gain.exponentialRampToValueAtTime(scaled, peakAt);
                gain.gain.exponentialRampToValueAtTime(SILENT, at + tone.duration);

                osc.connect(gain);
                gain.connect(audio.destination);

                osc.start(at);

                /*
                    【必ず止めること】止めないと、鳴り終わったあとも
                    オシレーターが動き続け、鳴らすたびに溜まっていく
                */
                osc.stop(at + tone.duration);

                at += tone.duration;
            });

            /* 合成音の長さ＝並べた音の合計（at は now から進めてきた） */
            return at - now;
        }

        /*
            鳴らして、鳴り終わってから done を呼ぶ（leaveThenRun / enterThenRun の中身）。
            待つのは LEAVE_WAIT_MAX_MS まで。鳴らなかったら待たない（理由は leaveThenRun のコメント）
        */
        function playThenRun(spec, done) {

            var seconds = play(spec);

            if (!seconds) {
                done();
                return;
            }

            var waitMs = Math.min(seconds * 1000, LEAVE_WAIT_MAX_MS);
            var ran = false;

            setTimeout(function () {
                if (!ran) {
                    ran = true;
                    done();
                }
            }, waitMs);
        }

        return {

            /* 入室・退室はドアの音（mp3）。読めなければ合成音の「ド→ソ」「ソ→ド」に落ちる */
            enter: function () { play({ fileKey: 'enter', tones: TONE_ENTER, volume: VOLUME_ENTER }); },
            leave: function () { play({ fileKey: 'leave', tones: TONE_LEAVE, volume: VOLUME_LEAVE }); },

            /* 発言は合成音のまま（ファイルは用意していない） */
            message: function () { play({ fileKey: null, tones: TONE_MESSAGE, volume: VOLUME_MESSAGE }); },

            /*
                ノック。部屋の中で、誰かが満室の部屋にノックしたときに鳴らす。
                ファイル（knock.mp3。FTP で手置き）が読めなければ、合成音の「コン、コン」に落ちる
            */
            knock: function () { play({ fileKey: 'knock', tones: TONE_KNOCK, volume: VOLUME_KNOCK }); },

            /*
                === 退室ボタン用。鳴らして、鳴り終わってから呼び戻す ===

                【なぜ、この形が要るのか】
                退室ボタンを押すとラウンジへ移動してしまうので、
                **普通に鳴らすと、鳴り始めた瞬間にページごと消える。**
                「鳴らす場所そのものが無い」として一度は見送ったが、
「**退室の処理の前に鳴らせないか**」で、
                **移動を待たせる**という3つ目のやり方が出てきた。

                【鳴らないときは、待たない】
                通知音がオフ・まだ一度も操作していない・直前の音と近すぎる——
                どの理由でも play が 0 を返すので、**そのまま素通りする。**
                「音を鳴らすかどうか」の判断を、呼んだ側へ持ち出さないための形である。

                【必ず上限を掛けること】
                音が長い・時計が進まない・呼び戻しが来ない、のどれが起きても
                **退室できなくなってはいけない。**
                鳴り終わるより先に上限へ達したら、そこで切り上げて先へ進む
            */
            leaveThenRun: function (done) {
                playThenRun({ fileKey: 'leave', tones: TONE_LEAVE, volume: VOLUME_LEAVE }, done);
            },

            /*
                === 入ってくる音を鳴らして、鳴り終わってから呼び戻す===

                トップページで名前を決めて「ラウンジに移動」を押したときに使う。
                退室ボタンとまったく同じ事情（押すと移動してしまう）なので、同じ形・同じ上限にしてある
            */
            enterThenRun: function (done) {
                playThenRun({ fileKey: 'enter', tones: TONE_ENTER, volume: VOLUME_ENTER }, done);
            },

            /*
                オン/オフを切り替える

                オンにするのは設定パネルを操作した直後＝ブラウザが音を許す
                きっかけになる瞬間なので、ここでAudioContextまで用意しておく。

                【効果音を読みに行くのも、ここだけ】
                既定はオフなので、鳴らさない人には1バイトも送らない
            */
            setEnabled: function (value) {
                enabled = !!value;
                if (enabled) {
                    ensureContext();
                    preloadSoundFiles();
                }
            },

            /*
                音量の段階（'low' / 'normal' / 'high'）を切り替える。
                **実際に適用した段階の名前を返す**

                【白リストは、ここでも通す】
                渡ってくるのはCookieかサーバーの設定で、どちらもサーバー側で
                丸めてある。それでもここで見るのは、Cookieが利用者の手元にあり、
                中身を自由に書き換えられるため。知らない名前なら既定（中）に落とす。

                【返り値がある理由】呼び出し側は、これをそのまま
                セレクトボックスへ入れればよい。落とした結果を返すので、
                「倍率は中なのに、セレクトはどれも選ばれていない」という
                食い違いが起こりようがない
            */
            setVolumeLevel: function (level) {

                var known = Object.prototype.hasOwnProperty.call(VOLUME_SCALES, level);
                var applied = known ? level : DEFAULT_VOLUME_LEVEL;

                volumeScale = VOLUME_SCALES[applied];

                return applied;
            },

            /*
                選べる3つのどれかかどうか
                （Cookieを使うか、サーバーの既定値へ落とすかの判断に使う）
            */
            isKnownLevel: function (level) {
                return Object.prototype.hasOwnProperty.call(VOLUME_SCALES, level);
            },

            /* 自動再生の制限をほどく（下の unlock の登録から呼ばれる） */
            unlock: function () {
                if (enabled) {
                    ensureContext();
                }
            }
        };
    }


    /*
        === 保存してある設定を読む（ラウンジ・トップページ用）===

        Cookie の名前と「読めないときは既定へ」の規則は、部屋（chat-room.js の
        SOUND_COOKIE_NAME / SOUND_VOLUME_COOKIE_NAME のあたり）とまったく同じにしてある。
        **部屋の側を変えたら、ここも直すこと**（部屋は設定パネルと一緒に読み書きするので、
        自分で持っている）
    */
    var ENABLED_COOKIE_NAME = 'ChatSoundEnabled';
    var VOLUME_COOKIE_NAME = 'ChatSoundVolume';

    function readCookie(name) {
        var match = document.cookie.match(new RegExp('(?:^|; )' + name + '=([^;]*)'));
        return match ? match[1] : null;
    }

    /*
        鳴らすか（Cookie が '1' か '0'。無ければサイトの既定＝SiteSettings の NotificationSoundDefault）

        【罠】「Cookie が無い」と「'0' が入っている」は別物である
        （まとめてオフ扱いにすると、既定をオンに変えた日に、自分でオフにした人の設定まで覆る）
    */
    function readSavedEnabled(defaultOn) {

        var saved = readCookie(ENABLED_COOKIE_NAME);

        return (saved === null) ? (defaultOn === true) : (saved === '1');
    }

    /* 音量の段階（知らない名前・無ければ、サイトの既定＝NotificationSoundVolumeDefault） */
    function readSavedVolumeLevel(sounds, defaultLevel) {

        var saved = readCookie(VOLUME_COOKIE_NAME);

        return sounds.isKnownLevel(saved) ? saved : defaultLevel;
    }

    /*
        最初の操作（クリック・キー・タッチ）を1回だけ捕まえて、自動再生の制限をほどく。
        部屋（chat-room.js の「自動再生の制限をほどく」）と同じ形（click を足した理由も、あちらに書いてある）
    */
    function unlockOnFirstInput(sounds) {

        ['pointerdown', 'keydown', 'touchstart', 'click'].forEach(function (eventName) {
            document.addEventListener(eventName, function () {
                sounds.unlock();
            }, { once: true, capture: true });
        });
    }


    window.VbChatSounds = {
        create: create,
        readSavedEnabled: readSavedEnabled,
        readSavedVolumeLevel: readSavedVolumeLevel,
        unlockOnFirstInput: unlockOnFirstInput
    };

})(window);