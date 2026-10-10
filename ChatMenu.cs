using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace AskTheAlmanac;

/// <summary>The chat window, built from the game's own UI pieces so it looks like a vanilla menu.</summary>
internal sealed class ChatMenu : IClickableMenu
{
    private const string Title = "Ask the Almanac";
    private const string Placeholder = "Ask anything... e.g. how much is a coop? (/help for commands)";
    private const string Intro = "Hello, farmer! Ask me anything about the valley: prices, gifts, crops, fish, bundles...";

    /// <summary>Space between the frame edge and the content.</summary>
    private const int Pad = 36;
    private const int ScrollBarWidth = 44;
    private const int BodyIndent = 16;
    private const int ChipGap = 12;

    private static readonly Color YouColor = new(30, 80, 160);
    private static readonly Color ErrorColor = new(170, 30, 30);
    private static readonly Color StatusColor = new(110, 90, 70);

    private readonly ChatSession session;
    private readonly Func<string?> getGameContext;
    private readonly TextBox input;
    private readonly List<Row> rows = new();
    private readonly List<(Rectangle Bounds, string Label, string Question)> chips = new();
    private int hoveredChip = -1;
    private HashSet<char>? fontChars;

    private ClickableTextureComponent sendButton = null!;
    private ClickableTextureComponent upArrow = null!;
    private ClickableTextureComponent downArrow = null!;
    private Rectangle logArea;
    private Rectangle scrollRunner;
    private int rowHeight;
    private int visibleRows;
    private int scrollRow;
    private int seenVersion = -1;

    /// <param name="Thinking">Draw the session status with animated dots instead of <paramref name="Text"/>.</param>
    private readonly record struct Row(string Text, Color Color, int Indent = 0, bool Bold = false, bool Thinking = false);

    private int MaxScroll => Math.Max(0, rows.Count - visibleRows);

    public ChatMenu(ChatSession session, Func<string?> getGameContext)
        : base(0, 0, 0, 0, showUpperRightCloseButton: true)
    {
        this.session = session;
        this.getGameContext = getGameContext;

        input = new TextBox(Game1.content.Load<Texture2D>("LooseSprites\\textBox"), null, Game1.smallFont, Game1.textColor)
        {
            textLimit = 300,
        };
        input.OnEnterPressed += _ => Submit();

        Layout();
        FocusInput();
    }

    /*********
    ** Input
    *********/
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound); // close button

        int chip = chips.FindIndex(c => c.Bounds.Contains(x, y));
        if (chip >= 0 && !session.IsBusy)
            Ask(chips[chip].Question);
        else if (sendButton.containsPoint(x, y))
            Submit();
        else if (upArrow.containsPoint(x, y))
            Scroll(-1, playSound);
        else if (downArrow.containsPoint(x, y))
            Scroll(1, playSound);
        else if (scrollRunner.Contains(x, y) && MaxScroll > 0)
            scrollRow = (int)Math.Round((y - scrollRunner.Y) / (float)scrollRunner.Height * MaxScroll);

        FocusInput();
    }

    public override void receiveScrollWheelAction(int direction)
    {
        Scroll(direction > 0 ? -3 : 3);
    }

    public override void receiveKeyPress(Keys key)
    {
        // Everything else is typed into the text box via the keyboard dispatcher, so don't let the base
        // class close the menu on 'E' or trigger other game hotkeys.
        switch (key)
        {
            case Keys.Escape:
                exitThisMenu();
                break;
            case Keys.PageUp:
                Scroll(-Math.Max(1, visibleRows - 1));
                break;
            case Keys.PageDown:
                Scroll(Math.Max(1, visibleRows - 1));
                break;
            case Keys.Up:
                Scroll(-1);
                break;
            case Keys.Down:
                Scroll(1);
                break;
        }
    }

    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);
        sendButton.tryHover(x, y);
        hoveredChip = chips.FindIndex(c => c.Bounds.Contains(x, y));
        upArrow.tryHover(x, y);
        downArrow.tryHover(x, y);
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        Layout();
    }

    protected override void cleanupBeforeExit()
    {
        input.Selected = false;
        if (Game1.keyboardDispatcher.Subscriber == input)
            Game1.keyboardDispatcher.Subscriber = null;
        base.cleanupBeforeExit();
    }

    /*********
    ** Update and draw
    *********/
    public override void update(GameTime time)
    {
        base.update(time);

        if (session.Version != seenVersion)
        {
            seenVersion = session.Version;
            RebuildRows();
            scrollRow = MaxScroll;
        }
    }

    public override void draw(SpriteBatch b)
    {
        // background dim, frame and title banner
        b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.5f);
        drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), xPositionOnScreen, yPositionOnScreen, width, height, Color.White);
        SpriteText.drawStringWithScrollCenteredAt(b, Title, xPositionOnScreen + width / 2, yPositionOnScreen - 64);

        // conversation
        int y = logArea.Y;
        for (int i = scrollRow; i < Math.Min(rows.Count, scrollRow + visibleRows); i++)
        {
            Row row = rows[i];
            string text = row.Thinking
                ? Sanitize(session.Status ?? "Thinking") + new string('.', (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 400 % 4))
                : row.Text;
            var position = new Vector2(logArea.X + row.Indent, y);

            if (row.Bold)
                Utility.drawBoldText(b, text, Game1.smallFont, position, row.Color);
            else
                Utility.drawTextWithShadow(b, text, Game1.smallFont, position, row.Color);
            y += rowHeight;
        }

        // scroll bar
        if (MaxScroll > 0)
        {
            upArrow.draw(b);
            downArrow.draw(b);
            drawTextureBox(b, Game1.mouseCursors, new Rectangle(403, 383, 6, 6), scrollRunner.X, scrollRunner.Y, scrollRunner.Width, scrollRunner.Height, Color.White, 4f, false);
            int thumbY = scrollRunner.Y + (int)((scrollRunner.Height - 40) * (scrollRow / (float)MaxScroll));
            b.Draw(Game1.mouseCursors, new Rectangle(scrollRunner.X, thumbY, 24, 40), new Rectangle(435, 463, 6, 10), Color.White);
        }

        // suggested follow-up questions
        for (int i = 0; i < chips.Count; i++)
        {
            var (bounds, label, _) = chips[i];
            drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), bounds.X, bounds.Y, bounds.Width, bounds.Height, i == hoveredChip ? Color.Wheat : Color.White, 4f, false);
            Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(bounds.X + 16, bounds.Y + (bounds.Height - Game1.smallFont.LineSpacing) / 2), Game1.textColor);
        }

        // input
        input.Draw(b);
        if (input.Text.Length == 0)
            Utility.drawTextWithShadow(b, Placeholder, Game1.smallFont, new Vector2(input.X + 16, input.Y + 8), Game1.textColor * 0.45f, shadowIntensity: 0f);
        sendButton.draw(b, session.IsBusy ? Color.Gray : Color.White, 0.88f);

        base.draw(b); // close button
        drawMouse(b);
    }

    /*********
    ** Private methods
    *********/
    private void Layout()
    {
        width = Math.Min(1100, Game1.uiViewport.Width - 128);
        height = Math.Min(760, Game1.uiViewport.Height - 160);
        Vector2 topLeft = Utility.getTopLeftPositionForCenteringOnScreen(width, height);
        xPositionOnScreen = (int)topLeft.X;
        yPositionOnScreen = (int)topLeft.Y + 32; // leave room for the title banner
        initializeUpperRightCloseButton();

        int left = xPositionOnScreen + Pad;
        int right = xPositionOnScreen + width - Pad;

        input.X = left;
        input.Y = yPositionOnScreen + height - Pad - 48;
        input.Width = right - left - 64 - 16;
        sendButton = new ClickableTextureComponent(
            new Rectangle(input.X + input.Width + 16, input.Y - 8, 64, 64),
            Game1.mouseCursors,
            Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 46),
            1f
        );

        logArea = new Rectangle(left, yPositionOnScreen + Pad, right - left - ScrollBarWidth - 12, 0);
        LayoutLog(input.Y - 24);
        seenVersion = -1; // re-wrap text for the new width
    }

    /// <summary>Size the conversation area and its scroll bar to end at the given Y position.</summary>
    private void LayoutLog(int bottom)
    {
        logArea.Height = bottom - logArea.Y;
        upArrow = new ClickableTextureComponent(new Rectangle(logArea.Right + 12, logArea.Y, 44, 48), Game1.mouseCursors, new Rectangle(421, 459, 11, 12), 4f);
        downArrow = new ClickableTextureComponent(new Rectangle(logArea.Right + 12, logArea.Bottom - 48, 44, 48), Game1.mouseCursors, new Rectangle(421, 472, 11, 12), 4f);
        scrollRunner = new Rectangle(upArrow.bounds.X + 10, upArrow.bounds.Bottom + 4, 24, downArrow.bounds.Y - upArrow.bounds.Bottom - 8);

        rowHeight = Game1.smallFont.LineSpacing;
        visibleRows = Math.Max(1, logArea.Height / rowHeight);
    }

    /// <summary>Lay out the follow-up question buttons above the input box, wrapping onto more lines as needed.</summary>
    private void LayoutChips()
    {
        chips.Clear();
        hoveredChip = -1;
        if (session.IsBusy || session.Suggestions.Count == 0)
        {
            LayoutLog(input.Y - 24);
            return;
        }

        int left = logArea.X;
        int maxWidth = sendButton.bounds.Right - left;
        int chipHeight = Game1.smallFont.LineSpacing + 24;

        // place chips relative to y = 0, then shift them up so the last line sits just above the input box
        var placed = new List<(Rectangle, string, string)>();
        int x = left, y = 0;
        foreach (string question in session.Suggestions)
        {
            string label = Truncate(Sanitize(question), maxWidth - 32);
            int chipWidth = (int)Game1.smallFont.MeasureString(label).X + 32;
            if (x > left && x + chipWidth > left + maxWidth)
            {
                x = left;
                y += chipHeight + ChipGap;
            }
            placed.Add((new Rectangle(x, y, chipWidth, chipHeight), label, question));
            x += chipWidth + ChipGap;
        }

        int top = input.Y - 20 - (y + chipHeight);
        foreach (var (bounds, label, question) in placed)
            chips.Add((new Rectangle(bounds.X, bounds.Y + top, bounds.Width, bounds.Height), label, question));
        LayoutLog(top - 16);
    }

    private static string Truncate(string text, int maxWidth)
    {
        if (Game1.smallFont.MeasureString(text).X <= maxWidth)
            return text;
        while (text.Length > 0 && Game1.smallFont.MeasureString(text + "...").X > maxWidth)
            text = text[..^1];
        return text.TrimEnd() + "...";
    }

    private void RebuildRows()
    {
        LayoutChips();
        rows.Clear();
        int wrapWidth = logArea.Width - BodyIndent - 8;

        if (session.Lines.Count == 0)
            AddWrapped(Intro, StatusColor, wrapWidth);

        foreach (ChatLine line in session.Lines)
        {
            (string label, Color color) = line.Speaker switch
            {
                Speaker.You => ("You", YouColor),
                Speaker.Error => ("Almanac", ErrorColor),
                Speaker.Info => ("", StatusColor),
                _ => ("Almanac", Game1.textColor),
            };
            if (line.Speaker == Speaker.Info)
            {
                // command output: no speaker label, muted colour
                AddWrapped(Sanitize(line.Text), StatusColor, wrapWidth);
                rows.Add(new Row("", Color.White));
                continue;
            }

            rows.Add(new Row(label, color, Bold: true));
            AddWrapped(Sanitize(line.Text), line.Speaker == Speaker.Error ? ErrorColor : Game1.textColor, wrapWidth, BodyIndent);
            rows.Add(new Row("", Color.White));
        }

        if (session.IsBusy)
        {
            rows.Add(new Row("Almanac", Game1.textColor, Bold: true));
            rows.Add(new Row("", StatusColor, BodyIndent, Thinking: true));
        }
    }

    private void AddWrapped(string text, Color color, int wrapWidth, int indent = 0)
    {
        foreach (string paragraph in text.Split('\n'))
        {
            if (paragraph.Trim().Length == 0)
            {
                rows.Add(new Row("", color));
                continue;
            }
            foreach (string line in Game1.parseText(paragraph, Game1.smallFont, wrapWidth).Split('\n'))
                rows.Add(new Row(line, color, indent));
        }
    }

    private void Submit()
    {
        string question = input.Text.Trim();
        if (question.Length == 0)
            return;

        // commands work even while an answer is loading, so /clear can cancel it
        string? command = session.RunCommand(question);
        if (command != null)
        {
            input.Text = "";
            Game1.playSound(command == "clear" ? "trashcan" : "smallSelect");
            return;
        }

        if (session.IsBusy)
            return;
        input.Text = "";
        Ask(question);
    }

    private void Ask(string question)
    {
        session.Ask(question, getGameContext());
        Game1.playSound("smallSelect");
    }

    private void Scroll(int delta, bool playSound = false)
    {
        int newRow = Math.Clamp(scrollRow + delta, 0, MaxScroll);
        if (newRow != scrollRow && playSound)
            Game1.playSound("shwip");
        scrollRow = newRow;
    }

    private void FocusInput()
    {
        input.Selected = true;
        Game1.keyboardDispatcher.Subscriber = input;
    }

    /// <summary>Strip markdown the model may still produce, and replace characters the game font can't draw.</summary>
    private string Sanitize(string text)
    {
        fontChars ??= new HashSet<char>(Game1.smallFont.Characters);

        text = text.Replace("**", "").Replace("__", "").Replace("`", "");
        text = Regex.Replace(text, @"^\s*#+\s*", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"^(\s*)[*•]\s+", "$1- ", RegexOptions.Multiline);

        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            string? replacement = ch switch
            {
                '‘' or '’' => "'",
                '“' or '”' => "\"",
                '–' or '—' => "-",
                '…' => "...",
                '×' => "x",
                '\t' => "  ",
                '\r' => "",
                _ => null,
            };

            if (replacement != null)
                sb.Append(replacement);
            else if (ch == '\n' || fontChars.Contains(ch))
                sb.Append(ch);
        }
        return sb.ToString();
    }
}
