<%@ Page Language="vb" MasterPageFile="~/Accounts/AdminMaster.Master" AutoEventWireup="false" ValidateRequest="false" MaintainScrollPositionOnPostback="true" CodeBehind="Profile.aspx.vb" Inherits="vb_chat.AccountProfilePage" Title="プロフィール" %>

<%-- 【ValidateRequest="false" にしてある】（ブログ型への作り替えの①）
     ASP.NET は既定で「<」のすぐ後ろに英字が来る入力を、ページに届く前に 500 で止める。
     自己紹介は自由に書く欄で、タグの話を書く人もいる。
     切ってよい理由＝この画面が出す値は、全部エスケープしている（asp:TextBox の既定と、出口のエスケープ）。
     **記事の本文と違い、プロフィールに生の HTML を出す場所は1つも無い。**
     切った画面は、これで9枚目（記事の編集＋アカウントの6画面＋問い合わせ＋ここ）。
     **Web.config でサイト全体を切らないこと**

     【MaintainScrollPositionOnPostback="true"】（2026年9月29日の直し）
     保存が断られてポストバックで画面が戻ってきたとき、**読んでいた場所に留まる**。
     指摘：保存ボタンを押したあと、画面の位置が上に戻ってしまう。
     うまくいったときは 302 で移り直すので、そちらは Scripts/profile-editor.js が場所を戻す
     （記事の編集画面と同じ形） --%>

<asp:Content ID="BodyContent" ContentPlaceHolderID="AdminBody" runat="server">

    <%-- 【コメントは、必ずサーバー側コメントで書くこと】
         （「//」や HTML のコメントで書くと、そのまま利用者のブラウザへ届く）

         プロフィールの編集（ブログ型への作り替えの①）。

         【項目の並びは、②で作る公開ページと同じにしてある】
         見比べながら直せるようにするため。**②を作るときも、この順番にそろえること**
         （カバー → アイコン → 名前（表示名） → 自己紹介 → 基本情報 → SNS → ブログ）。

         【イントリンシック・デザインで組んである】（「常設の観点」）
         幅の数字（@media）を1つも書いていない。項目は grid の auto-fit と minmax で、
         入るなら2列、入らなければ1列に**中身が決める**。

         【項目の中は、2つに分かれている】
         「項目名」と「欄のまとまり（.profile-field-main）」。折り返しはまとまりの中だけで起きるので、
         次の行へ送られた欄も、**項目名の右（＝ほかの欄と同じ位置）**から始まる。
         **欄と「非公開」は、必ずこのまとまりの中に入れること**——外に出すと、
         折り返したときに項目名と同じ左端まで戻ってしまう（実機で踏んだ）。
         **説明（.profile-hint）だけは、まとまりの外**（行いっぱいを使うため）。

         【「非公開」のチェックが、どの項目のものか分かるようにしてある】
         見える文字は「非公開」だけだが、その前に読み上げソフトだけが読む文字を置いてある。
         文言はコードビハインドの PrivateLabel が組み立てる。
         **チェックを1つ足すときは、そこにも1行足すこと**——足さないと、
         耳で聞いている人には「非公開 非公開 非公開」としか聞こえない

         【知らせは、いちばん下の「保存の帯」に1本化してある】
         保存した・画像を差し替えた・画像を外した・断った理由の4つとも、帯の中に出す。
         **画面の上に知らせを足さないこと**——出る場所が2つあると「ちぐはぐ」になる（開発者の報告） --%>

    <div class="admin-box profile-box">

        <h1>プロフィール</h1>

        <%-- 【公開のプロフィールへのリンクは、保存の帯へ移した】
             記事の編集画面（Q。指摘：スクロールの手間が大きい）と同じ理由・同じ形にそろえた。
             保存すると元の位置に戻るので、いちばん上のリンクは画面の外になる --%>

        <p class="profile-lead">
            ここで入れたものが、プロフィールのページとブログに出ます。
            <strong>名前のほかは、すべて任意です。</strong>
            項目ごとに「非公開」にできます（<strong>非公開にしても、入れた内容は残ります</strong>）。
        </p>

        <%-- ============================================================
             【並び】カバー画像 → アイコン → 名前（組み直した）
             ============================================================

             【asp:Panel の DefaultButton】どの欄で Enter を押しても「保存する」が押されるように。
             決めないと、form のいちばん上のボタン（カバー画像の「外す」）が押される。
             （Panel の始まりを画像の区切りの前へ動かした＝アイコンの「文字」の欄で Enter を押しても「外す」にならないように）

             【保存の仕方が2つある】画像（カバー・アイコンの写真）は**選んだ時点で差し替わる**。
             アイコンの「文字・色」と、名前から下は「保存する」で保存する --%>
        <asp:Panel ID="ProfilePanel" runat="server" DefaultButton="SaveButton">

            <%-- --- カバー画像（選んだ時点で差し替わる）--- --%>
            <section class="profile-section" aria-labelledby="profileCoverHeading">

                <h2 id="profileCoverHeading">カバー画像</h2>

                <%-- 【説明文は短く。見た目の位置で指さない】（「常設の観点」） --%>
                <p class="profile-hint">
                    <strong>選んだ時点で差し替わります</strong>（「保存する」は要りません）。
                    前の画像は、そのときに消えます。<%: ImageFormatsText %>が使えます。
                </p>

                <%-- 【src は出口でエスケープする】組み立てるのは AccountProfileImages.SrcOf の1か所。
                     【空のときは、画像ではなく案内を出す】data-empty で切り替えるのは、
                     あとから JavaScript が（画面を読み込み直さずに）出し分けるため --%>
                <div class="profile-cover-frame" id="profileCoverFrame" data-empty="<%: CoverEmptyFlag %>">
                    <img id="profileCoverImg" class="profile-cover-img" src="<%: CoverSrc %>" alt="" />
                    <span class="profile-image-empty">まだ選んでいません</span>
                </div>

                <div class="profile-image-actions">
                    <%-- 【name を付けていない file の欄】送信には乗らない。
                         画像は JavaScript（Scripts/profile-editor.js）が、別の受け口へ送る --%>
                    <label class="profile-file-btn">
                        <span>画像を選ぶ</span>
                        <input type="file" id="profileCoverFile" accept="image/*"
                               data-profile-image="cover" />
                    </label>
                    <asp:Button ID="CoverRemoveButton" runat="server" Text="外す"
                                CssClass="admin-link-btn" CausesValidation="False" />
                </div>

            </section>

            <%-- --- アイコン（指摘：位置が分かりにくい）---

                 【丸は1つだけ＝今の自分のアイコン】右上の角・ブログ・プロフィールに出るものと同じ見た目。
                   写真がある … 写真
                   写真が無い … 文字のアイコン（文字・色を変えると、その場で変わる）
                 それまでは「アイコン画像（灰色の丸に『まだ選んでいません』）」と、離れたところの「文字のアイコン」の見本で、丸が2つあった。

                 【丸の横に、操作を全部まとめた】写真（画像を選ぶ・外す）と、文字・色（最初の色に戻す）。
                 入りきらない幅では、丸の下へ回る（.avatar-editor の flex-wrap。幅の数字は書いていない）。

                 【写真を置いている間の「文字・色」は、隠さずに出したまま、小さな見本を添える】
                 「写真を外すと、この文字と色になります」の1行と、その文字の小さな丸。
                   ・隠すと、写真を外すまで「文字のアイコン」があることに気づけない
                   ・薄くすると、押せないように見える（押せるのに）
                 写真が無い間は、この1行は出ない（大きな丸そのものが、文字のアイコンを見せている）。
                 出し分けは CSS の :has（丸の data-empty を見る）だけで、写真を選んだ瞬間も JS を足さずに切り替わる。

                 決まりと確かめ（1文字か・色の形）は Accounts/AccountAvatar.vb。丸も同じ部品で描いている。
                 data-* は、JS（Scripts/profile-editor.js の「5」）が「空にしたとき」に戻す最初の文字・色 --%>
            <section class="profile-section" aria-labelledby="profileAvatarHeading">

                <h2 id="profileAvatarHeading">アイコン</h2>

                <div class="avatar-editor" id="avatarLetterPreview"
                     data-default-letter="<%: DefaultLetter %>"
                     data-default-color="<%: DefaultColor %>"
                     data-user-name="<%: CurrentUserName %>">

                    <%-- 今のアイコン。写真の img と、文字のアイコンの両方を置いて、data-empty で出し分ける
                         （写真を選ぶ・外すの JS は、今までどおり data-empty を書き換えるだけ） --%>
                    <div class="profile-avatar-frame" id="profileAvatarFrame" data-empty="<%: AvatarEmptyFlag %>">
                        <img id="profileAvatarImg" class="profile-avatar-img" src="<%: AvatarSrc %>" alt="" />
                        <span class="avatar-letter-main js-letter-preview" aria-hidden="true"><%= LetterPreviewHtml("xl") %></span>
                    </div>

                    <div class="avatar-editor-controls">

                        <%-- 写真（選んだ時点で差し替わる） --%>
                        <div class="avatar-editor-row">
                            <span class="profile-label" id="avatarPhotoLabel">写真</span>
                            <div class="profile-image-actions" role="group" aria-labelledby="avatarPhotoLabel">
                                <label class="profile-file-btn">
                                    <span>画像を選ぶ</span>
                                    <input type="file" id="profileAvatarFile" accept="image/*"
                                           data-profile-image="avatar" />
                                </label>
                                <asp:Button ID="AvatarRemoveButton" runat="server" Text="外す"
                                            CssClass="admin-link-btn" CausesValidation="False" />
                            </div>
                        </div>
                        <p class="profile-hint avatar-editor-hint">
                            <strong>選んだ時点で差し替わります</strong>。正方形に切って保存します（真ん中を残します）。
                        </p>

                        <%-- 写真を置いている間だけ出る1行（出し分けは CSS の :has） --%>
                        <p class="avatar-when-photo">
                            <span class="avatar-letter-mini js-letter-preview" aria-hidden="true"><%= LetterPreviewHtml("xs") %></span>
                            写真を外すと、この文字と色になります。
                        </p>

                        <div class="profile-grid avatar-letter-fields">

                            <div class="profile-field">
                                <asp:Label ID="AvatarLetterLabel" runat="server" AssociatedControlID="AvatarLetterBox"
                                           CssClass="profile-label" Text="文字" />
                                <div class="profile-field-main">
                                    <%-- 【MaxLength は 16】見た目の1文字を入れる器（絵文字は中で2文字以上のことがある）。
                                         2文字目を打ったら、JS が新しく打ったほうの1文字だけを残す --%>
                                    <asp:TextBox ID="AvatarLetterBox" runat="server" CssClass="profile-control avatar-letter-input"
                                                 MaxLength="16" autocomplete="off" />
                                </div>
                            </div>

                            <div class="profile-field">
                                <asp:Label ID="AvatarColorLabel" runat="server" AssociatedControlID="AvatarColorBox"
                                           CssClass="profile-label" Text="色" />
                                <div class="profile-field-main">
                                    <%-- 【readonly】押すと色を選ぶ道具が開く（トップページのアイコンの色と同じ形）。
                                         readonly はコードビハインドの Attributes で付けている。
                                         【markup に readonly="..." と書かないこと】asp:TextBox の ReadOnly プロパティと見なされ
                                         （書いた文字は真偽値にならずプリコンパイルで止まる）、仮に True にすると**送られた値を読まなくなる** --%>
                                    <asp:TextBox ID="AvatarColorBox" runat="server" CssClass="profile-control avatar-color-input"
                                                 MaxLength="7" data-jscolor="{}" />
                                    <button type="button" id="avatarColorReset" class="admin-link-btn">最初の色に戻す</button>
                                </div>
                            </div>

                        </div>

                        <p class="profile-hint avatar-editor-hint">
                            文字と色は「保存する」で保存します。文字を空にすると、最初の文字に戻ります。
                        </p>

                    </div>

                </div>

            </section>

            <%-- --- 名前（表示名）--- --%>
            <section class="profile-section" aria-labelledby="profileNameHeading">

                <h2 id="profileNameHeading">名前（表示名）</h2>

                <div class="profile-grid">

                    <%-- 【項目名を置いていない】（指摘：どちらも重複して見える）
                         この区切りの中に欄は1つだけで、見出し（h2）と同じ言葉になるため。
                         **読み上げソフトには、aria-labelledby で見出しの文字を欄の名前として伝えている**
                         ——見た目で消すだけ（.visually-hidden）にすると、同じ言葉を2回読むことになる --%>
                    <div class="profile-field profile-field--wide">
                        <div class="profile-field-main">
                            <asp:TextBox ID="DisplayNameBox" runat="server" CssClass="profile-control"
                                         MaxLength="50" autocomplete="nickname"
                                         aria-labelledby="profileNameHeading" />
                        </div>
                        <p class="profile-hint">
                            ブログやプロフィール、自分のアイコンのメニューに出る名前です（50文字まで）。
                        </p>
                    </div>

                </div>

            </section>


            <%-- --- 自己紹介 --- --%>
            <section class="profile-section" aria-labelledby="profileIntroHeading">

                <h2 id="profileIntroHeading">自己紹介</h2>

                <%-- 【包んだ値を入れる、見えない欄】（Scripts/site.js の「7」）
                     本番の手前の WAF が、タグを含む POST を 403 で止めるため。
                     data-encode-on-submit と data-encoded-prefix は、コードビハインドから入れている
                     （runat="server" の要素の属性に、サーバー側の式を書くとページが止まる） --%>
                <asp:HiddenField ID="IntroductionEncoded" runat="server" />

                <div class="profile-grid">

                    <%-- 項目名を置いていない理由は、上の「名前（表示名）」と同じ --%>
                    <div class="profile-field profile-field--wide">
                        <div class="profile-field-main">
                            <asp:TextBox ID="IntroductionBox" runat="server" CssClass="profile-control profile-textarea"
                                         TextMode="MultiLine" Rows="8" EnableViewState="False"
                                         aria-labelledby="profileIntroHeading" />
                            <asp:CheckBox ID="IntroductionPrivate" runat="server" CssClass="profile-private" />
                        </div>
                        <p class="profile-hint">
                            <asp:Literal ID="IntroductionHint" runat="server" />
                        </p>
                    </div>

                </div>

            </section>

            <%-- --- 基本情報 --- --%>
            <section class="profile-section" aria-labelledby="profileBasicHeading">

                <h2 id="profileBasicHeading">基本情報</h2>

                <div class="profile-grid">

                    <div class="profile-field">
                        <asp:Label ID="GenderLabel" runat="server" AssociatedControlID="GenderList"
                                   CssClass="profile-label" Text="性別" />
                        <div class="profile-field-main">
                            <asp:DropDownList ID="GenderList" runat="server" CssClass="profile-control" />
                            <asp:CheckBox ID="GenderPrivate" runat="server" CssClass="profile-private" />
                        </div>
                    </div>

                    <div class="profile-field">
                        <asp:Label ID="BloodTypeLabel" runat="server" AssociatedControlID="BloodTypeList"
                                   CssClass="profile-label" Text="血液型" />
                        <div class="profile-field-main">
                            <asp:DropDownList ID="BloodTypeList" runat="server" CssClass="profile-control" />
                            <asp:CheckBox ID="BloodTypePrivate" runat="server" CssClass="profile-private" />
                        </div>
                    </div>

                    <%-- 生年月日だけは、「非公開」のチェックではなく3択。
                         【名前を付けた3択にしてある】「出さない／月日だけ／年月日」。
                         「設定後は変更不可」の形にはしない（あとから何度でも変えられる）。
                         【2つの欄は、必ず同じまとまりの中に置くこと】外に出すと、狭い画面で
                         「出しかた」の欄が項目名と同じ左端から始まって、別の項目に見える（実機で踏んだ） --%>
                    <%-- 【お試しのあと同じ日に採用】カレンダー（TextMode="Date"）をやめ、年・月・日の数字の欄を3つにした
                         （指摘：1971年までカレンダーでさかのぼるのは大変。GOV.UK の「生年月日にカレンダーを使わない」形）。
                         開発者が開発機・Galaxy で確かめて採用（基本ルール【D】）。DB の列（日付の型）は変えていない。
                         【type="number" にしない】上下の矢印・ホイールで値が変わる。数字のキーボードは inputmode="numeric" で出す。
                         【数字の欄の決まり】data-number-field（前後の空白を省く・全角を半角に。Scripts/site.js の「16」・Core/NumberInput.vb）。
                         範囲（data-number-min／max）は書かない＝端へ黙って寄せず、サーバーが理由を出して断る（Profile.aspx.vb の ReadBirthDate）。
                         【3つで1つのまとまり】role="group" ＋ 項目名。読み上げの名前は「生年月日の年」のように aria-label で。
                         理由は3つの欄の上（data-error-anchor＝この行のまとまり）に出す。
                         【並び】年・月・日は離さない（.profile-birth-parts は折り返さない）。入らない幅では「出しかた」だけが次の行へ --%>
                    <div class="profile-field profile-field--wide" role="group" aria-labelledby="birthDateGroupLabel">
                        <span class="profile-label" id="birthDateGroupLabel">生年月日</span>
                        <div class="profile-field-main" id="birthDateMain">
                            <span class="profile-birth-parts">
                                <%-- 【「西暦」はいつも見える字で】欄の中の薄い字は打つと消えるので使わない。
                                     読み上げは欄の名前（「生年月日の年（西暦）」）で伝えるので、この字は aria-hidden --%>
                                <span class="profile-birth-unit profile-birth-era" aria-hidden="true">西暦</span>
                                <asp:TextBox ID="BirthYearBox" runat="server" CssClass="profile-control profile-birth-input profile-birth-input--year"
                                             MaxLength="4" inputmode="numeric" autocomplete="bday-year" aria-label="生年月日の年（西暦）"
                                             data-number-field="1" data-number-default="" data-error-anchor="birthDateMain" />
                                <span class="profile-birth-unit" aria-hidden="true">年</span>
                                <asp:TextBox ID="BirthMonthBox" runat="server" CssClass="profile-control profile-birth-input"
                                             MaxLength="2" inputmode="numeric" autocomplete="bday-month" aria-label="生年月日の月"
                                             data-number-field="1" data-number-default="" data-error-anchor="birthDateMain" />
                                <span class="profile-birth-unit" aria-hidden="true">月</span>
                                <asp:TextBox ID="BirthDayBox" runat="server" CssClass="profile-control profile-birth-input"
                                             MaxLength="2" inputmode="numeric" autocomplete="bday-day" aria-label="生年月日の日"
                                             data-number-field="1" data-number-default="" data-error-anchor="birthDateMain" />
                                <span class="profile-birth-unit" aria-hidden="true">日</span>
                            </span>
                            <%-- 【出しかた（2択）と「非公開」は1組】（決定：ほかの項目と同じ「非公開」のチェックにする）。
                                 入らない幅では、この1組が年・月・日の次の行へ回る（項目名の下には回らない） --%>
                            <span class="profile-birth-options">
                                <asp:DropDownList ID="BirthDateModeList" runat="server"
                                                  CssClass="profile-control profile-control--mode" />
                                <asp:CheckBox ID="BirthDatePrivate" runat="server" CssClass="profile-private" />
                            </span>
                        </div>
                        <%-- 【「右の欄が『出しかた』です」をやめた】
                             選ぶ中身（出さない／月日だけ／年月日）を見れば分かるうえ、
                             狭い画面では右ではなく下に来る。読み上げソフトには aria-label で伝えている --%>
                        <p class="profile-hint">
                            あとから何度でも変えられます。
                        </p>
                    </div>

                    <div class="profile-field">
                        <asp:Label ID="HometownLabel" runat="server" AssociatedControlID="HometownList"
                                   CssClass="profile-label" Text="出身地" />
                        <div class="profile-field-main">
                            <asp:DropDownList ID="HometownList" runat="server" CssClass="profile-control" />
                            <asp:CheckBox ID="HometownPrivate" runat="server" CssClass="profile-private" />
                        </div>
                    </div>

                    <div class="profile-field">
                        <asp:Label ID="ResidenceLabel" runat="server" AssociatedControlID="ResidenceList"
                                   CssClass="profile-label" Text="居住地" />
                        <div class="profile-field-main">
                            <asp:DropDownList ID="ResidenceList" runat="server" CssClass="profile-control" />
                            <asp:CheckBox ID="ResidencePrivate" runat="server" CssClass="profile-private" />
                        </div>
                    </div>

                    <div class="profile-field">
                        <asp:Label ID="RelationshipLabel" runat="server" AssociatedControlID="RelationshipList"
                                   CssClass="profile-label" Text="ステータス" />
                        <div class="profile-field-main">
                            <asp:DropDownList ID="RelationshipList" runat="server" CssClass="profile-control" />
                            <asp:CheckBox ID="RelationshipPrivate" runat="server" CssClass="profile-private" />
                        </div>
                    </div>

                    <%-- 職業は「選ぶ ＋ 自由記入」（よくあるプロフィールの形）。
                         選ばずに自由記入だけ、も許す（当てはまるものが無い人を止めないため） --%>
                    <div class="profile-field profile-field--wide profile-field--occupation">
                        <asp:Label ID="OccupationLabel" runat="server" AssociatedControlID="OccupationList"
                                   CssClass="profile-label" Text="職業" />
                        <div class="profile-field-main">
                            <asp:DropDownList ID="OccupationList" runat="server"
                                              CssClass="profile-control profile-control--occupation" />
                            <%-- 【説明の行をやめて、欄の中のグレーの字（placeholder）にした】
                                 「右の欄は自由記入です」と書いていたが、狭い画面では右ではなく下に来る。
                                 placeholder は打つと消えるので、**読み上げソフト用に aria-label を付けてある**
                                 （Profile.aspx.vb。placeholder も同じところで入れている） --%>
                            <asp:TextBox ID="OccupationTextBox" runat="server"
                                         CssClass="profile-control profile-control--occupation-text"
                                         MaxLength="50" />
                            <asp:CheckBox ID="OccupationPrivate" runat="server" CssClass="profile-private" />
                        </div>
                    </div>

                </div>

            </section>

            <%-- --- SNS --- --%>
            <section class="profile-section" aria-labelledby="profileSnsHeading">

                <h2 id="profileSnsHeading">SNS</h2>

                <p class="profile-hint">
                    そのSNSでのID（vbchat）でも、アドレス（https://x.com/vbchat）でも入れられます。
                </p>

                <div class="profile-grid">

                    <div class="profile-field">
                        <asp:Label ID="XLabel" runat="server" AssociatedControlID="XBox"
                                   CssClass="profile-label" Text="X（旧 Twitter）" />
                        <div class="profile-field-main">
                            <asp:TextBox ID="XBox" runat="server" CssClass="profile-control" MaxLength="120" />
                            <asp:CheckBox ID="XPrivate" runat="server" CssClass="profile-private" />
                        </div>
                    </div>

                    <div class="profile-field">
                        <asp:Label ID="FacebookLabel" runat="server" AssociatedControlID="FacebookBox"
                                   CssClass="profile-label" Text="Facebook" />
                        <div class="profile-field-main">
                            <asp:TextBox ID="FacebookBox" runat="server" CssClass="profile-control" MaxLength="120" />
                            <asp:CheckBox ID="FacebookPrivate" runat="server" CssClass="profile-private" />
                        </div>
                    </div>

                    <div class="profile-field">
                        <asp:Label ID="InstagramLabel" runat="server" AssociatedControlID="InstagramBox"
                                   CssClass="profile-label" Text="Instagram" />
                        <div class="profile-field-main">
                            <asp:TextBox ID="InstagramBox" runat="server" CssClass="profile-control" MaxLength="120" />
                            <asp:CheckBox ID="InstagramPrivate" runat="server" CssClass="profile-private" />
                        </div>
                    </div>

                </div>

            </section>

            <%-- 【ブログのタイトルと説明は、ブログの設定（/account/blog/settings）へ移した】
                 プロフィールに残るのは、アイコン・自己紹介（誕生日などの基本情報）・SNS。DB の列（AccountProfiles.BlogTitle・BlogDescription）は
                 そのままで、置く画面だけを変えた。プロフィールの保存は、この2列に触らない（AccountProfileStore.Save） --%>

            <%-- 【保存の帯。画面の下に貼り付いて、スクロールしても常に見える】
                 要望：記事の編集画面と同じく、保存ボタンの帯にする。スクロールの手間が大きいため。
                 **記事の編集画面と同じ部品を使っている**（Content/Site.css の .admin-savebar・
                 .admin-form-status・.admin-saved-status）。写しではなく、同じクラスを指しているだけ。

                 【スマホで文字を打っている間も、帯はキーボードのすぐ上に残る】
                 記事の編集画面は、打っている間だけ貼り付けをやめる（is-typing）。あちらはツールバーの右端に
                 保存のボタンが出るので困らないが、**この画面にはツールバーが無い**ので、同じことをすると
                 保存のボタンと、断った理由がどこにも見えなくなる（指摘：ボタンの帯が見えなくなる）。
                 打っている欄が帯の裏に隠れないようにするのは、Scripts/profile-editor.js の「3」。

                 【知らせは、この2行に出す】上の「うまくいった」と、下の「断った理由」。
                 どちらも空のときは場所を取らない（:empty で display: none） --%>
            <div id="profileSaveBar" class="admin-savebar">

                <%-- 【保存のボタンと「公開ページを見る」の組】（記事の編集画面の .admin-savebar-main と同じ）
                     リンクは（ブログ型への作り替えの②の直し）に足したもの。要望：
                     プロフィールの編集の画面に、公開のプロフィールのページをすぐ確かめられるリンクを置く。
                     【新しいタブで開く】書きかけの内容を置いて移動させないため。
                     【行き先はコードビハインドが決める】URL を組み立てるのは AccountProfileRules.PublicUrl の1か所 --%>
                <div class="admin-savebar-main">
                    <asp:Button ID="SaveButton" runat="server" Text="保存する" CssClass="account-submit" />
                    <asp:HyperLink ID="ViewLink" runat="server" CssClass="admin-link-btn"
                                   Target="_blank" rel="noopener" Text="公開ページを見る" />
                </div>

                <%-- 保存した・画像を差し替えた・画像を外した。
                     【role="status"】読み上げソフトに伝える。ただし「開いた時点から入っている文」は読まれない決まりなので、
                     Scripts/profile-editor.js が、いったん空にしてから入れ直している（記事の編集画面と同じ手） --%>
                <p id="profileSavedStatus" class="admin-saved-status" role="status"><asp:Literal ID="DoneText" runat="server" /></p>

                <%-- 断った理由。【aria-live="assertive"】その場で読み上げる。
                     【data-error-jump】（2026年9月30日の夕方）悪かった欄が分かるとき（aria-invalid="true" が付いている）は、
                     Scripts/site.js の「9」が、**理由をその欄の上へ移して**、そこへカーソルを置く
                     （決まり＝理由は入力欄の上。帯に出したままだと、スマホではキーボードに隠れる）。
                     どの欄とも言えない理由（混み合っています・画像の失敗）は、この帯に出たまま（帯はいつも見えている） --%>
                <p id="profileErrorStatus" class="admin-form-status" aria-live="assertive" data-error-jump="1"><asp:Literal ID="ErrorText" runat="server" /></p>

            </div>

            <div class="profile-actions">
                <asp:HyperLink ID="HomeLink" runat="server" NavigateUrl="~/account" CssClass="admin-link-btn"
                               Text="管理画面へ戻る" />
            </div>

        </asp:Panel>

    </div>

</asp:Content>

<asp:Content ID="ScriptsContent" ContentPlaceHolderID="AdminScripts" runat="server">

    <%-- 【画像を送る仕掛け】選ばれた画像を、画面側で縮めてから（アイコンは正方形に切ってから）
         Accounts/UploadProfileImage.aspx へ送る。記事の画像と同じ考え方である。
         受け口の URL と大きさの決まりは、隠した箱の data-* で渡す（1か所で持つため） --%>
    <div id="profileImageConfig"
         data-upload-url="<%: UploadUrl %>"
         data-max-bytes="<%: MaxImageBytes %>"
         data-avatar-edge="<%: AvatarMaxEdge %>"
         data-cover-edge="<%: CoverMaxEdge %>"
         hidden="hidden"></div>

    <%-- 【色を選ぶ道具（jscolor）】文字のアイコンの「色」。トップページのアイコンの色と同じ道具・同じ見た目。
         色見本は、最初の色の一覧（Accounts/AccountAvatar.vb の DefaultPalette）と同じ14色 --%>
    <script src="<%: Asset("~/Scripts/lib/jscolor.min.js") %>"></script>
    <script>
        jscolor.presets.default = {
            palette: [
                '#E53935', '#D81B60', '#8E24AA', '#5E35B1', '#3949AB', '#1E88E5', '#039BE5',
                '#00897B', '#43A047', '#7CB342', '#F4511E', '#FB8C00', '#6D4C41', '#546E7A'
            ],
            paletteCols: 7,
            width: 181,
            height: 101,
            closeButton: true,
            closeText: '閉じる'
        };
    </script>

    <script src="<%: Asset("~/Scripts/profile-editor.js") %>"></script>

</asp:Content>
