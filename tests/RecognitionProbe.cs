using System;
using System.IO;
using System.Linq;
namespace LayoutPilot {
  static class RecognitionProbe {
    private static void Main(){
      var maps=KeyboardMap.Installed();
      var recognition=new Recognition();
      Check(recognition,maps,Language.English,"dct","все");
      Check(recognition,maps,Language.English,"dct,","все,");
      Check(recognition,maps,Language.English,"ghbdtn","привет");
      Check(recognition,maps,Language.English,"gthtbvtyeq","переименуй");
      Check(recognition,maps,Language.English,"gthtbvtyeb","переименуй");
      Check(recognition,maps,Language.English,"Gthtbvtyeb,","Переименуй,");
      Check(recognition,maps,Language.Russian,"переименуи","переименуй");
      Check(recognition,maps,Language.Russian,"переименуй",null);
      Check(recognition,maps,Language.English,"ltg","деп");
      Check(recognition,maps,Language.English,"[hjvt","хроме");
      Check(recognition,maps,Language.English,"ghbbtn","привет");
      Check(recognition,maps,Language.English,"ghtrbym","прикинь");
      Check(recognition,maps,Language.Russian,"прекинь","прикинь");
      Check(recognition,maps,Language.Russian,"еше","ещё");
      Check(recognition,maps,Language.Russian,"Еше,","Ещё,");
      Check(recognition,maps,Language.Russian,"ЕШЕ!","ЕЩЁ!");
      Check(recognition,maps,Language.Russian,"рудь","руль", true);
      Check(recognition,maps,Language.Russian,"ешы",null);
      Check(recognition,maps,Language.Russian,"нуы","yes");
      Check(recognition,maps,Language.Kazakh,"еше","ещё");
      Check(recognition,maps,Language.English,"tit",null);
      var english=maps.Single(map=>map.Language==Language.English);
      var russian=maps.Single(map=>map.Language==Language.Russian);
      var kazakh=maps.Single(map=>map.Language==Language.Kazakh);
      Check(recognition,maps,Language.Russian,"Салем!","Сәлем!",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"калайсын","қалайсың",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"казакша","қазақша",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"бугин","бүгін",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"жаксы","жақсы",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"сурак","сұрақ",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"Кужат,","Құжат,",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"мугалим","мұғалім",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"жургызушы","жүргізуші",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"касыпорын","кәсіпорын",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"жатакхана","жатақхана",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"Кубылыс,","Құбылыс,",target:Language.Kazakh);
      Check(recognition,maps,Language.Kazakh,"гажайып","ғажайып",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"жумбак","жұмбақ",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"Костилдилык,","Қостілділік,",target:Language.Kazakh);
      Check(recognition,maps,Language.Kazakh,"кырагы","қырағы",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"мумкиншилик","мүмкіншілік",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"Мейиримдилик!","Мейірімділік!",target:Language.Kazakh);
      Check(recognition,maps,Language.Kazakh,"бейымделгыштык","бейімделгіштік",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"талкылауга","талқылауға",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"копше","көпше",target:Language.Kazakh);
      Check(recognition,maps,Language.Kazakh,"сундет","сүндет",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"ушин",null);
      Check(recognition,maps,Language.Russian,"кулын",null);
      if (recognition.ConvertContext("ушин",Keys(russian,"ушин"),russian,kazakh)!=null)
        throw new Exception("An ambiguous Kazakh spelling should be left alone in context");
      Check(recognition,maps,Language.Russian,"ответственность",null);
      Check(recognition,maps,Language.Kazakh,"тусиникти","түсінікті",target:Language.Kazakh);
      Check(recognition,maps,Language.Kazakh,"калайсын","қалайсың",target:Language.Kazakh);
      Check(recognition,maps,Language.Russian,"как",null);
      Check(recognition,maps,Language.Russian,"казак",null);
      Check(recognition,maps,Language.Russian,"кулак",null);
      Check(recognition,maps,Language.Russian,"тенге",null);
      Check(recognition,maps,Language.Russian,"42",null);
      var kazakhKeys=Keys(kazakh,"сәлем");
      var kazakhOnRussian=Recognition.Convert(kazakhKeys,kazakhKeys.Length,russian);
      Check(recognition,maps,Language.Russian,kazakhOnRussian,"сәлем",target:Language.Kazakh);
      if (recognition.ConvertContext("калайсын,",Keys(russian,"калайсын,"),russian,kazakh)!="қалайсың,")
        throw new Exception("A previous Kazakh word should preserve punctuation when its letters are restored");
      Check(recognition,new[] {english,russian},Language.Russian,"салем",null);
      if (recognition.ConvertContext("еше",Keys(russian,"еше"),russian,english)!=null)
        throw new Exception("A Russian typo should not be converted to tit in context");
      if (recognition.ConvertContext("рудь",Keys(russian,"рудь"),russian,english)!=null)
        throw new Exception("A listed typo should not be converted in context");
      if (recognition.ConvertContext("ешы",Keys(russian,"ешы"),russian,english)!=null)
        throw new Exception("A rare short layout match should be left alone in context");
      if (recognition.ConvertContext("ghtrbym",Keys(english,"ghtrbym"),english,russian)!="прикинь")
        throw new Exception("A previous word should also be corrected in context");
      Console.WriteLine("PASS ghtrbym in context");
      Check(recognition,maps,Language.English,"helllo","hello");
      Check(recognition,maps,Language.Russian,"ошыбок","ошибок");
      Check(recognition,maps,Language.Russian,"харашо","хорошо");
      Check(recognition,maps,Language.Russian,"КАНЕШНО!","КОНЕЧНО!");
      var wrongLayout=Recognition.Convert(Keys(russian,"канешно"),7,english);
      Check(recognition,maps,Language.English,wrongLayout,"конечно");
      Check(recognition,maps,Language.Russian,"хорошо",null);
      Check(recognition,maps,Language.English,"becose","because", true);
      Check(recognition,maps,Language.English,"NECCESSARY!","NECESSARY!", true);
      Check(recognition,maps,Language.Kazakh,"тәңертен","таңертең", true);
      Check(recognition,maps,Language.Kazakh,"журегім","жүрегім", true);
      var wrongEnglish=Recognition.Convert(Keys(english,"becose"),6,russian);
      Check(recognition,maps,Language.Russian,wrongEnglish,"because", true);
      var wrongKazakh=Recognition.Convert(Keys(kazakh,"тәңертен"),8,english);
      Check(recognition,maps,Language.English,wrongKazakh,"таңертең", true);
      Check(recognition,maps,Language.Kazakh,"таңертең",null);
      Check(recognition,maps,Language.English,"because",null);
      Check(recognition,maps,Language.English,"hello",null);
      Check(recognition,maps,Language.English,"www.chrome.com",null);
      CheckPersonalDictionary(maps,english,russian);
    }

    private static void CheckPersonalDictionary(KeyboardMap[] maps, KeyboardMap english, KeyboardMap russian) {
      string directory = Path.Combine(Path.GetTempPath(), "SpaceFixDictionaryTest-" + Guid.NewGuid().ToString("N"));
      string path = Path.Combine(directory, "personal-dictionary.tsv");
      try {
        var dictionary = new PersonalDictionary(path);
        var recognition = new Recognition(dictionary);
        Check(recognition,maps,Language.English,"dct","все");
        dictionary.Set("dct",null);
        dictionary.Set("спс","Спасибо за помощь!");
        dictionary.Set("салем","сәлем");
        dictionary.Set("x","Example");
        Check(recognition,maps,Language.English,"DCT,",null);
        Check(recognition,maps,Language.Russian,"СПС,","Спасибо за помощь!,");
        Check(recognition,maps,Language.Kazakh,"салем","сәлем");
        dictionary.Set("салем",null);
        dictionary.Set("сурак",null);
        Check(recognition,maps,Language.Russian,"салем",null);
        Check(recognition,maps,Language.Russian,"сурак",null);
        Check(recognition,maps,Language.Kazakh,"салем",null);
        Check(recognition,maps,Language.English,"x","Example");
        if (recognition.ConvertContext("dct",Keys(english,"dct"),english,russian)!=null)
          throw new Exception("An exception should also protect a word in context");
        var reloaded = new PersonalDictionary(path);
        if (reloaded.LoadFailed || !reloaded.HasSingleLetterRules) throw new Exception("Personal dictionary did not reload");
        recognition = new Recognition(reloaded);
        Check(recognition,maps,Language.English,"dct",null);
        Check(recognition,maps,Language.Russian,"спс","Спасибо за помощь!");
        reloaded.Set("DCT","custom text");
        Check(recognition,maps,Language.English,"dct","custom text");
        reloaded.Remove("dct");
        reloaded.Remove("x");
        if (reloaded.HasSingleLetterRules) throw new Exception("A removed rule should stop matching");
        Check(recognition,maps,Language.English,"dct","все");
        Check(new Recognition(new PersonalDictionary(path)),maps,Language.English,"dct","все");
      } finally {
        File.Delete(path);
        if (Directory.Exists(directory)) Directory.Delete(directory);
      }
      Console.WriteLine("PASS personal dictionary precedence, exceptions, updates and restart");
    }

    private static void Check(Recognition recognition, KeyboardMap[] maps, Language language, string input, string expected,
      bool listed=false, Language? target=null) {
      var source=maps.Single(map=>map.Language==language);
      var choice=recognition.Find(input,Keys(source,input),source,maps);
      var actual=choice==null?null:choice.Text;
      if (actual!=expected) throw new Exception(input+" -> "+actual+" expected "+expected);
      if (listed && choice.Rank!=-1) throw new Exception(input+" did not use the imported typo dictionary");
      if (target.HasValue && (choice==null || choice.Map.Language!=target.Value))
        throw new Exception(input+" did not use the expected layout "+target.Value);
      Console.WriteLine("PASS "+input+" -> "+(actual??input));
    }
    private static int[] Keys(KeyboardMap source, string input) {
      return input.Select(ch=>Enumerable.Range(0,512).First(k=>source.At(k)==ch)).ToArray();
    }
  }
}
