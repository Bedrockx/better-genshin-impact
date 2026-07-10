using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoWood.Assets;

public class AutoWoodAssets : BaseAssets<AutoWoodAssets>
{
    public RecognitionObject TheBoonOfTheElderTreeRo;
    public RecognitionObject BoonRo;

    // public RecognitionObject CharacterGuideRo;
    public RecognitionObject MenuBagRo;

    public RecognitionObject ConfirmRo;
    public RecognitionObject EnterGameRo;
    public RecognitionObject ExitSwitchRo;

    // 木头数字1/2/3/4/5/6/7/9/12/15/18/21
    public RecognitionObject WoodCountRo ;
    public Rect WoodCountUpperRectArray ;
    
    public RecognitionObject WoodCountMRo ;
    public Rect WoodCountUpperRectMArray ;

    // 木头数量
    public Rect WoodCountUpperRect;

    private AutoWoodAssets()
    {

        WoodCountUpperRect = new Rect((int)(100 * AssetScale), (int)(450 * AssetScale), (int)(300 * AssetScale), (int)(250 * AssetScale));

        //「王树瑞佑」
        TheBoonOfTheElderTreeRo = new RecognitionObject
        {
            Name = "TheBoonOfTheElderTree",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "TheBoonOfTheElderTree.png"),
            RegionOfInterest = new Rect(CaptureRect.Width - CaptureRect.Width / 4, CaptureRect.Height / 2,
                CaptureRect.Width / 4, CaptureRect.Height - CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();

        // CharacterGuideRo = new RecognitionObject
        // {
        //     Name = "CharacterGuide",
        //     RecognitionType = RecognitionTypes.TemplateMatch,
        //     TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "character_guide.png"),
        //     RegionOfInterest = new Rect(0, 0, CaptureRect.Width / 2, CaptureRect.Height),
        //     DrawOnWindow = false
        // }.InitTemplate();

        MenuBagRo = new RecognitionObject
        {
            Name = "MenuBag",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "menu_bag.png"),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width / 2, CaptureRect.Height),
            DrawOnWindow = false
        }.InitTemplate();

        ConfirmRo = new RecognitionObject
        {
            Name = "AutoWoodConfirm",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "confirm.png"),
            DrawOnWindow = false
        }.InitTemplate();

        EnterGameRo = new RecognitionObject
        {
            Name = "EnterGame",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "exit_welcome.png"),
            RegionOfInterest = new Rect(0, CaptureRect.Height / 2, CaptureRect.Width, CaptureRect.Height - CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();
        ExitSwitchRo = new RecognitionObject
        {
            Name = "ExitSwitch",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "exit_switch.png"),
            RegionOfInterest = new Rect(CaptureRect.Width*9/10, (int)(CaptureRect.Height*0.85), CaptureRect.Width/10,(int)(CaptureRect.Height*0.15)),
            DrawOnWindow = false
        }.InitTemplate();
        BoonRo = new RecognitionObject
        {
            Name = "Boon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "boon.png"),
            RegionOfInterest = new Rect((int)(CaptureRect.Width*1/10), (int)(CaptureRect.Height*0.05), (int)(CaptureRect.Width*7/10),(int)(CaptureRect.Height*0.8)),
            DrawOnWindow = false
        }.InitTemplate();
    }
    
    public RecognitionObject InitializeWoodCountRecognitionObject(int index = 1, int areIndex = 1, double thresholdN = 0.7)
    {
        WoodCountRo = new RecognitionObject
        {
            Name = index.ToString(),
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", index + ".png"),
            RegionOfInterest = InitializeWoodCountUpperRectArray(areIndex),
            UseMask = true,
            Threshold = thresholdN,
            DrawOnWindow = true
        }.InitTemplate();
        return  WoodCountRo;
    }  
    
    public RecognitionObject InitializeWoodCountRecognitionObjectM(string wood, int areIndex, double threshold = 0.8)
    {
        WoodCountMRo = new RecognitionObject
        {
            Name = wood,
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", wood + ".png"),
            RegionOfInterest = InitializeWoodsUpperRectArray(areIndex),
            UseMask = true,
            Threshold = threshold,
            DrawOnWindow = true
        }.InitTemplate();
        return  WoodCountMRo;
    }  
    
    private OpenCvSharp.Rect InitializeWoodCountUpperRectArray(int areIndex)
    {
        WoodCountUpperRectArray = new Rect((int)(CaptureRect.Width * 0.1),
                (int)(CaptureRect.Height * 0.5) + (areIndex - 1) * (int)(CaptureRect.Height * 0.046),
                (int)(CaptureRect.Width * 0.05)+(int)(CaptureRect.Width * 0.012)*2, (int)(CaptureRect.Height * 0.046));
      
        return WoodCountUpperRectArray;
    }
    
    private OpenCvSharp.Rect InitializeWoodsUpperRectArray(int areIndex)
    {
        WoodCountUpperRectMArray = new Rect((int)(CaptureRect.Width * 0.08),
            (int)(CaptureRect.Height * 0.5) + (areIndex - 1) * (int)(CaptureRect.Height * 0.046),
            (int)(CaptureRect.Width * 0.043), (int)(CaptureRect.Height * 0.046));
      
        return WoodCountUpperRectMArray;
    }
}
