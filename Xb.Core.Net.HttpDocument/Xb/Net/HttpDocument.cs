using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

//TODO: インスタンス生成時点でクエリしたい。生成完了後、HtmlDocumentを切り出す操作が本クラスの主タスク。

namespace Xb.Net
{
    public class HttpDocument : Xb.Net.Http
    {
        /// <summary>
        /// HTML要素検出時の検索方向型
        /// </summary>
        /// <remarks></remarks>
        public enum SearchDirection
        {
            /// <summary>
            /// 下位方向。子ノードを走査する。
            /// </summary>
            /// <remarks></remarks>
            Lower,

            /// <summary>
            /// 上位方向。親ノードを走査する。
            /// </summary>
            /// <remarks></remarks>
            Upper
        }

        /// <summary>
        /// HTML文字列切り出し方法
        /// </summary>
        /// <remarks></remarks>
        public enum SliceType
        {
            /// <summary>
            /// 加工しない。
            /// </summary>
            /// <remarks></remarks>
            NotSlice,

            /// <summary>
            /// 改行文字を整形する。
            /// </summary>
            /// <remarks></remarks>
            FormatLinefeed,

            /// <summary>
            /// 改行整形後に半角英字・記号を削除し、マルチバイト文字＋数値を切り出す。
            /// </summary>
            /// <remarks></remarks>
            MultiByteAndNumber,

            /// <summary>
            /// 改行整形後に半角英字・記号・半角数値を削除し、マルチバイト文字のみを切り出す。
            /// </summary>
            /// <remarks></remarks>
            MultiByteOnly
        }

        public HtmlAgilityPack.HtmlDocument Document { get; private set; }


        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="url"></param>
        /// <param name="values"></param>
        /// <param name="method"></param>
        /// <param name="headers"></param>
        public HttpDocument(string url,
                            string values = null,
                            Xb.Net.Http.MethodType method = Xb.Net.Http.MethodType.Post,
                            Dictionary<HttpRequestHeader, string> headers = null) 
            : base(url, 
                   values, 
                   method, 
                   headers)
        {
            Task<HtmlAgilityPack.HtmlDocument> task = this.GetAsync();
        }

        

        /// <summary>
        /// 渡し値URLの内容を、文字列で取得する。
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        /// <remarks>
        /// <!--
        /// 注）HtmlAgilityPack.dll に依存している。参照設定しておくこと
        /// 
        /// 例：
        ///   Dim html As HtmlAgilityPack.HtmlDocument = ClsHttp.GetHtmlString("http://www.atmarkit.co.jp/fdotnet/dotnettips/730dlgttype/dlgttype.html")
        ///   Xb.App.Out(html.DocumentNode.OuterHtml)
        /// 
        /// HtmlAgilityPackによるノードクエリ：
        ///   <html>タグ内にあるエレメントの中の、
        ///    <body>タグ内にあるエレメントの中の、
        ///        idが'body'である<div>タグ内にあるエレメントの中の、
        ///            全ての子エレメント内の中の、
        ///                idが'pageArea_02'である<div>タグ
        /// 　　　　↓
        ///   nodes = html.DocumentNode.SelectNodes("/html/body/div[@id='body']//div[@id='pregArea_02']")
        /// 
        /// 　指定ノードの全子要素のなかから、<a>タグを抽出する。
        /// 　　　　↓
        ///   tmpNodes = node.SelectNodes("./a[@href]")
        /// 
        ///   指定ノードの子ノードを順次取得する。
        /// 　　　　↓
        ///   For Each node As HtmlAgilityPack.HtmlNode In nodes.ChildNodes
        ///   End For
        /// 
        ///   ノードの種類、属性を判定する。
        ///     divタグ、かつstyle属性が存在するとき
        /// 　　　　↓
        ///     If ((node.Name = "div") And (node.GetAttributeValue("style", "no-style") <> "no-style")) Then ...
        /// -->
        /// </remarks>
        private new async Task<HtmlAgilityPack.HtmlDocument> GetAsync()
        {
            var responseSet = await this.GetResponseAsync();
            if (responseSet == null)
                return null;

            var bytes = new List<byte>();
            int bt = 0;

            //応答データを1バイトずつ読み込み、バイト配列に追記する
            while (true)
            {
                try
                {
                    //１バイト読み込む
                    bt = responseSet.Stream.ReadByte();
                }
                catch (Exception)
                {
                    responseSet.Dispose();
                    responseSet = null;
                    return null;
                }

                //ストリーム終了したとき、ループを出る
                if (bt == -1)
                    break; 
                bytes.Add(Convert.ToByte(bt));
            }

            //ストリームオブジェクトを破棄する。
            responseSet.Dispose();
            responseSet = null;

            System.Byte[] byteArray = bytes.ToArray();

            this.Document = new HtmlAgilityPack.HtmlDocument();
            this.Document.LoadHtml(Xb.Str.GetEncode(bytes.ToArray()).GetString(byteArray, 0, byteArray.Length));
            
            return this.Document;
        }


        /// <summary>
        /// 渡し値HtmlAgilityPack.HtmlNodeノードの次のノードを返す。
        /// </summary>
        /// <param name="htmlNode"></param>
        /// <param name="direction"></param>
        /// <returns></returns>
        /// <remarks>
        /// 　検索方向別の要素検出優先度
        /// 　　下位方向検索：direction = SearchDirection.Lower (デフォルト)
        /// 　　　１．渡し値の子ノード一つめ
        /// 　　　２．渡し値の次のノード
        /// 　　　３．渡し値の上位方向検索結果
        /// 
        /// 　　上位方向検索：direction = SearchDirection.Upper
        /// 　　　１．渡し値の次のノード
        /// 　　　２．渡し値の上位ノードの次のノード
        /// 　　　３．渡し値の上位ノードの、上位方向検索結果
        /// 　　　４．Nothing
        /// </remarks>
        public static HtmlAgilityPack.HtmlNode GetNextNode(
            HtmlAgilityPack.HtmlNode htmlNode, 
            SearchDirection direction = SearchDirection.Lower
        )
        {
            HtmlAgilityPack.HtmlNode objTmpNode = null;

            //同じレイヤーでの次要素が存在しないとき
            //要素の検索方向によって処理を分岐する。
            if ((direction != SearchDirection.Upper))
            {
                //要素を、上位から下位に向けて調べる場合

                //渡し値ノードの子ノードが存在するか否かで処理を分岐する。
                if ((htmlNode.ChildNodes.Count > 0))
                {
                    //渡し値ノードの子ノードが存在するとき、その先頭ノードを戻り値とする。
                    return htmlNode.ChildNodes[0];
                }

                //渡し値の子ノードが存在しないとき、渡し値要素と同レイヤーの次の要素を取得する。
                objTmpNode = htmlNode.NextSibling;
                if (((objTmpNode != null)))
                {
                    //同じレイヤーで次の要素が取得できたとき、その要素を戻り値とする。
                    return objTmpNode;
                }

                //渡し値ノードの子ノードが存在せず、かつ同レイヤーにも次の要素がないとき
                //再起処理にて、渡し値の上位ノードの次ノードを取得して戻り値とする。
                return Xb.Net.HttpDocument.GetNextNode(htmlNode, SearchDirection.Upper);

            }
            else
            {
                //要素を、下位から上位に向けて調べる場合

                //渡し値要素と同レイヤーの次の要素を取得する。
                objTmpNode = htmlNode.NextSibling;
                if (((objTmpNode != null)))
                {
                    //同じレイヤーで次の要素が取得できたとき、その要素を戻り値とする。
                    return objTmpNode;
                }

                //同レイヤーの次要素が無いとき、渡し値ノードの上位ノードを取得する。
                objTmpNode = htmlNode.ParentNode;

                //渡し値ノードの上位ノードが存在するか否かで処理を分岐する。
                if ((objTmpNode == null))
                {
                    //渡し値ノードの上位ノードが存在しないとき
                    //上位方向には何も存在しないので、Nothingを返す。
                    return null;
                }

                //上位ノードが存在する場合
                //上位ノードの、次ノードを取得する。
                objTmpNode = objTmpNode.NextSibling;
                if ((objTmpNode == null))
                {
                    //上位ノードの次ノードが取得できなかったとき、再起処理で上位ノードのさらに上のノードを取得する。
                    objTmpNode = htmlNode.ParentNode;
                    return Xb.Net.HttpDocument.GetNextNode(objTmpNode, SearchDirection.Upper);
                }

                //上位ノードの次のノードが取得できた場合。
                //取得されたノードを戻り値とする。
                return objTmpNode;
            }

        }


        /// <summary>
        /// HTML文字列を、渡し値条件でフォーマットして取得する。
        /// </summary>
        /// <param name="html"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        public static string GetSliceText(
            HtmlAgilityPack.HtmlDocument html, 
            SliceType slice = SliceType.MultiByteOnly, 
            Xb.Str.LinefeedType linefeed = Xb.Str.LinefeedType.Lf
        )
        {

            HtmlAgilityPack.HtmlNodeCollection objNodes = default(HtmlAgilityPack.HtmlNodeCollection);
            HtmlAgilityPack.HtmlNode objNode = default(HtmlAgilityPack.HtmlNode);
            string result = null;
            System.Text.RegularExpressions.Regex regexNonMb = null;
            System.Text.RegularExpressions.Regex regexNum = null;
            System.Text.RegularExpressions.Regex regexSerialLf = null;

            //半角英字・記号
            regexNonMb = new System.Text.RegularExpressions.Regex("[a-zA-Z\\t\\\\\\s\\-\\<\\>\\!\\?/\\*\\[\\]\\.\\(\\)%\\'\\=\\&\\#\\;\\:\\{\\}\\,\\+\\^\\|\\$@_" + "\"" + "]");
            //半角数値
            regexNum = new System.Text.RegularExpressions.Regex("\\d{7,}");
            //連続した改行
            regexSerialLf = new System.Text.RegularExpressions.Regex("\\n+");

            //Bodyノードを取得する。
            objNodes = html.DocumentNode.SelectNodes("//body");
            if (((objNodes != null)))
            {
                objNode = objNodes[0];
            }
            else
            {
                objNode = html.DocumentNode;
            }

            result = objNode.InnerText;

            //切り出し指定が未加工のとき、Body部分文字列をそのまま返す。
            //使わないかな...？
            if ((slice == SliceType.NotSlice))
                return result;

            //改行文字を一旦LFに統一する。
            result = result.Replace("\r\n", "\n").Replace("\r", "\n");
            result = regexSerialLf.Replace(result, "\n");

            //切り出し指定に合わせて加工する。
            if ((slice == SliceType.MultiByteAndNumber))
            {
                //切り出し指定がマルチバイトと数値のみ抽出のとき、半角英字・記号を削除する。
                result = regexNonMb.Replace(result, "");
                result = result.Replace("　", "");
            }
            else if ((slice == SliceType.MultiByteOnly))
            {
                //切り出し指定がマルチバイトのみ抽出のとき、半角英字・数値・記号を削除する。
                result = regexNonMb.Replace(result, "");
                result = regexNum.Replace(result, "");
                result = result.Replace("　", "");
            }

            if ((slice == SliceType.MultiByteOnly))
            {
                result = regexNonMb.Replace(result, "");
                result = regexNum.Replace(result, "");
                result = result.Replace("　", "");
            }

            //改行文字指定に合わせて再加工後に戻す。
            switch (linefeed)
            {
                case Xb.Str.LinefeedType.Lf:
                    return result;
                case Xb.Str.LinefeedType.CrLf:
                    return result.Replace("\n", "\r\n");
                case Xb.Str.LinefeedType.Cr:
                    return result.Replace("\n", "\r");
                default:
                    return result;
            }
        }
    }
}

