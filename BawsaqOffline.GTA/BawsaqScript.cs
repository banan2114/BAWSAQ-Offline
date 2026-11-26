
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using GTA;
using GTA.UI;

using BawsaqOffline.Core.Engine;
using BawsaqOffline.Core.Models;
using BawsaqOffline.Core.Trading;
using BawsaqOffline.Core.Persistence;
using BawsaqOffline.GTA;

public class BawsaqScript : Script
{
    private readonly MarketEngine market;
    private readonly Portfolio portfolio;
    private readonly SaveManager saveManager;
    private readonly List<Stock> bawsaqStocks;
    private readonly MissionDetector _missionDetector = new MissionDetector();

    private int selectedIndex;
    private bool menuOpen;

    private string lastStatus = "Welcome to BAWSAQ";

    private DateTime lastSaveTime;
    private DateTime lastMarketCheck;

    private static readonly TimeSpan AutoSaveInterval =
        TimeSpan.FromMinutes(2);

    private const float PanelX = 45f;
    private const float PanelY = 95f;
    private const float PanelWidth = 750f;
    private const float PanelHeight = 510f;

    private readonly Color background =
        Color.FromArgb(235, 13, 20, 27);

    private readonly Color panelSecondary =
        Color.FromArgb(255, 24, 34, 43);

    private readonly Color accent =
        Color.FromArgb(255, 40, 190, 160);

    private readonly Color selectedColor =
        Color.FromArgb(220, 32, 75, 75);

    private readonly Color white =
        Color.FromArgb(255, 240, 245, 248);

    private readonly Color muted =
        Color.FromArgb(255, 145, 160, 170);

    private readonly Color green =
        Color.FromArgb(255, 75, 210, 145);

    private readonly Color red =
        Color.FromArgb(255, 230, 90, 90);

    public BawsaqScript()
    {
        market = new MarketEngine();

        // Портфель общий для всех персонажей.
        // Деньги хранятся непосредственно в GTA V.
        portfolio = new Portfolio();

        // BaseDirectory уже указывает на папку scripts.
        string saveDirectory = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "BAWSAQOffline"
        );

        Directory.CreateDirectory(saveDirectory);

        string savePath = Path.Combine(
            saveDirectory,
            "save.json"
        );

        saveManager = new SaveManager(savePath);

        try
        {
            bool loaded = saveManager.Load(market, portfolio);

            if (loaded)
            {
                lastStatus = "Save loaded successfully";

                // Симулируем рынок за время отсутствия в игре.
                int processedTicks =
                    market.ProcessElapsedTime(DateTime.Now);

                if (processedTicks > 0)
                {
                    lastStatus =
                        "Market updated: " +
                        processedTicks +
                        " ticks";
                }
            }
            else
            {
                lastStatus = "New portfolio created";
                SaveGame();
            }
        }
        catch (Exception ex)
        {
            lastStatus = "LOAD ERROR: " + ex.Message;

            GTA.UI.Screen.ShowSubtitle(
                "BAWSAQ: save loading failed",
                5000
            );
        }

        bawsaqStocks = market
            .GetStocks(StockExchange.BAWSAQ)
            .ToList();

        selectedIndex = 0;
        menuOpen = false;

        lastSaveTime = DateTime.UtcNow;
        lastMarketCheck = DateTime.Now;

        KeyUp += OnKeyUp;
        Tick += OnTick;

        GTA.UI.Screen.ShowSubtitle(
            "BAWSAQ Offline initialized!",
            4000
        );
    }

    // --------------------------------------------------
    // GAME LOOP
    // --------------------------------------------------

    private void OnTick(object sender, EventArgs e)
    {
        DateTime now = DateTime.Now;

        _missionDetector.Tick();

        // Проверяем, прошло ли достаточно игрового времени
        // для очередного обновления рынка.
        if ((now - lastMarketCheck).TotalSeconds >= 1)
        {
            try
            {
                market.ProcessElapsedTime(now);
            }
            catch (Exception ex)
            {
                lastStatus = "MARKET ERROR: " + ex.Message;
            }

            lastMarketCheck = now;
        }

        // Автосохранение раз в две минуты.
        if (DateTime.UtcNow - lastSaveTime >= AutoSaveInterval)
        {
            SaveGame();
        }

        if (!menuOpen)
            return;

        Game.DisableAllControlsThisFrame();

        DrawMenu();
    }

    // --------------------------------------------------
    // INPUT
    // --------------------------------------------------

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F7)
        {
            menuOpen = !menuOpen;

            if (!menuOpen)
                SaveGame();

            return;
        }

        if (!menuOpen)
            return;

        if (e.KeyCode == Keys.Up)
        {
            selectedIndex--;

            if (selectedIndex < 0)
                selectedIndex = bawsaqStocks.Count - 1;
        }
        else if (e.KeyCode == Keys.Down)
        {
            selectedIndex++;

            if (selectedIndex >= bawsaqStocks.Count)
                selectedIndex = 0;
        }
        else if (e.KeyCode == Keys.B)
        {
            BuySelectedStock();
        }
        else if (e.KeyCode == Keys.S)
        {
            SellSelectedStock();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            menuOpen = false;
            SaveGame();
        }
    }

    // --------------------------------------------------
    // SAVE / LOAD
    // --------------------------------------------------

    private void SaveGame()
    {
        try
        {
            saveManager.Save(market, portfolio);
            lastSaveTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            lastStatus = "SAVE ERROR: " + ex.Message;
        }
    }

    // --------------------------------------------------
    // TRADING
    // --------------------------------------------------

    private int GetTransactionAmount(decimal amount)
    {
        decimal rounded = Math.Round(
            amount,
            0,
            MidpointRounding.AwayFromZero
        );

        if (rounded < 1m)
            rounded = 1m;

        if (rounded > int.MaxValue)
        {
            throw new InvalidOperationException(
                "Transaction amount is too large."
            );
        }

        return (int)rounded;
    }

    private void BuySelectedStock()
    {
        if (bawsaqStocks.Count == 0)
            return;

        Stock stock = bawsaqStocks[selectedIndex];

        try
        {
            const long quantity = 1;

            int cost = GetTransactionAmount(
                stock.Price * quantity
            );

            int currentCash = Game.Player.Money;

            if (cost > currentCash)
            {
                lastStatus = "NOT ENOUGH CHARACTER CASH";
                return;
            }

            if (!portfolio.Buy(stock, quantity))
            {
                lastStatus = "BUY FAILED";
                return;
            }

            try
            {
                Game.Player.Money = currentCash - cost;
            }
            catch
            {
                portfolio.Sell(stock, quantity);
                throw;
            }

            lastStatus =
                "Bought " + quantity + " " + stock.Symbol +
                " for $" + cost.ToString("N0");

            SaveGame();
        }
        catch (Exception ex)
        {
            lastStatus = "BUY FAILED: " + ex.Message;
        }
    }

    private void SellSelectedStock()
    {
        if (bawsaqStocks.Count == 0)
            return;

        Stock stock = bawsaqStocks[selectedIndex];

        try
        {
            const long quantity = 1;

            if (portfolio.GetShares(stock.Symbol) < quantity)
            {
                lastStatus = "NOT ENOUGH SHARES";
                return;
            }

            int payout = GetTransactionAmount(
                stock.Price * quantity
            );

            int currentCash = Game.Player.Money;

            long newCash = (long)currentCash + payout;

            if (newCash > int.MaxValue)
            {
                lastStatus = "CHARACTER CASH LIMIT";
                return;
            }

            if (!portfolio.Sell(stock, quantity))
            {
                lastStatus = "SELL FAILED";
                return;
            }

            try
            {
                Game.Player.Money = (int)newCash;
            }
            catch
            {
                portfolio.Buy(stock, quantity);
                throw;
            }

            lastStatus =
                "Sold " + quantity + " " + stock.Symbol +
                " for $" + payout.ToString("N0");

            SaveGame();
        }
        catch (Exception ex)
        {
            lastStatus = "SELL FAILED: " + ex.Message;
        }
    }

    // --------------------------------------------------
    // UI DRAWING
    // --------------------------------------------------

    private void DrawMenu()
    {
        if (bawsaqStocks.Count == 0)
        {
            DrawRectangle(
                PanelX,
                PanelY,
                PanelWidth,
                PanelHeight,
                background
            );

            DrawText(
                "BAWSAQ - NO STOCKS AVAILABLE",
                PanelX + 25,
                PanelY + 35,
                0.45f,
                white
            );

            return;
        }

        Stock selectedStock = bawsaqStocks[selectedIndex];

        // Main background.
        DrawRectangle(
            PanelX,
            PanelY,
            PanelWidth,
            PanelHeight,
            background
        );

        // Header.
        DrawRectangle(
            PanelX,
            PanelY,
            PanelWidth,
            5,
            accent
        );

        DrawText(
            "BAWSAQ",
            PanelX + 25,
            PanelY + 22,
            0.65f,
            accent
        );

        DrawText(
            "OFFLINE STOCK EXCHANGE",
            PanelX + 28,
            PanelY + 58,
            0.25f,
            muted
        );

        DrawText(
            "F7",
            PanelX + PanelWidth - 35,
            PanelY + 25,
            0.35f,
            muted,
            Alignment.Right
        );

        // Account information.
        DrawRectangle(
            PanelX + 20,
            PanelY + 95,
            PanelWidth - 40,
            75,
            panelSecondary
        );

        DrawText(
            "CHARACTER CASH",
            PanelX + 38,
            PanelY + 108,
            0.25f,
            muted
        );

        DrawText(
            "$" + Game.Player.Money.ToString("N0"),
            PanelX + 38,
            PanelY + 132,
            0.42f,
            white
        );

        DrawText(
            "SHARED PORTFOLIO VALUE",
            PanelX + 390,
            PanelY + 108,
            0.25f,
            muted
        );

        DrawText(
            "$" + portfolio.GetTotalValue(market).ToString("N2"),
            PanelX + 390,
            PanelY + 132,
            0.42f,
            accent
        );

        // Stock list section.
        DrawText(
            "MARKET",
            PanelX + 25,
            PanelY + 190,
            0.30f,
            muted
        );

        DrawText(
            "PRICE",
            PanelX + 290,
            PanelY + 190,
            0.25f,
            muted,
            Alignment.Right
        );

        DrawText(
            "CHANGE",
            PanelX + 390,
            PanelY + 190,
            0.25f,
            muted,
            Alignment.Right
        );

        // Stock rows.
        float rowY = PanelY + 220;
        const float rowHeight = 48;

        for (int i = 0; i < bawsaqStocks.Count; i++)
        {
            Stock stock = bawsaqStocks[i];

            float currentY = rowY + i * rowHeight;

            if (i == selectedIndex)
            {
                DrawRectangle(
                    PanelX + 20,
                    currentY - 5,
                    410,
                    42,
                    selectedColor
                );
            }

            DrawText(
                stock.Symbol,
                PanelX + 35,
                currentY,
                0.34f,
                i == selectedIndex ? accent : white
            );

            DrawText(
                stock.Name,
                PanelX + 105,
                currentY + 3,
                0.23f,
                muted
            );

            DrawText(
                "$" + stock.Price.ToString("N2"),
                PanelX + 290,
                currentY + 3,
                0.27f,
                white,
                Alignment.Right
            );

            Color changeColor =
                stock.ChangePercent >= 0 ? green : red;

            string changeText =
                (stock.ChangePercent >= 0 ? "+" : "") +
                stock.ChangePercent.ToString("N2") +
                "%";

            DrawText(
                changeText,
                PanelX + 390,
                currentY + 3,
                0.27f,
                changeColor,
                Alignment.Right
            );
        }

        // Selected stock details.
        float detailX = PanelX + 455;
        float detailY = PanelY + 190;

        DrawRectangle(
            detailX - 10,
            detailY - 5,
            270,
            215,
            panelSecondary
        );

        DrawText(
            "SELECTED STOCK",
            detailX + 5,
            detailY + 5,
            0.25f,
            muted
        );

        DrawText(
            selectedStock.Symbol,
            detailX + 5,
            detailY + 35,
            0.55f,
            accent
        );

        DrawText(
            selectedStock.Name,
            detailX + 5,
            detailY + 75,
            0.27f,
            white
        );

        DrawText(
            "PRICE",
            detailX + 5,
            detailY + 115,
            0.22f,
            muted
        );

        DrawText(
            "$" + selectedStock.Price.ToString("N2"),
            detailX + 250,
            detailY + 115,
            0.28f,
            white,
            Alignment.Right
        );

        DrawText(
            "YOUR SHARES",
            detailX + 5,
            detailY + 150,
            0.22f,
            muted
        );

        DrawText(
            portfolio.GetShares(selectedStock.Symbol).ToString("N0"),
            detailX + 250,
            detailY + 150,
            0.28f,
            white,
            Alignment.Right
        );

        // Status.
        DrawRectangle(
            PanelX + 20,
            PanelY + 440,
            PanelWidth - 40,
            35,
            panelSecondary
        );

        DrawText(
            lastStatus,
            PanelX + 35,
            PanelY + 448,
            0.23f,
            accent
        );

        // Footer controls.
        DrawText(
            "UP / DOWN  SELECT",
            PanelX + 25,
            PanelY + 485,
            0.22f,
            muted
        );

        DrawText(
            "B  BUY",
            PanelX + 260,
            PanelY + 485,
            0.22f,
            green
        );

        DrawText(
            "S  SELL",
            PanelX + 370,
            PanelY + 485,
            0.22f,
            red
        );

        DrawText(
            "ESC  CLOSE",
            PanelX + 500,
            PanelY + 485,
            0.22f,
            muted
        );
    }

    private void DrawRectangle(
        float x,
        float y,
        float width,
        float height,
        Color color)
    {
        var rectangle = new ContainerElement(
            new PointF(x, y),
            new SizeF(width, height),
            color
        );

        rectangle.Draw();
    }

    private void DrawText(
        string text,
        float x,
        float y,
        float scale,
        Color color,
        Alignment alignment = Alignment.Left)
    {
        var element = new TextElement(
            text,
            new PointF(x, y),
            scale,
            color,
            GTA.UI.Font.ChaletLondon,
            alignment,
            true,
            false
        );

        element.Draw();
    }
}
