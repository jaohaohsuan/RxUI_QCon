﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Media;
using ReactiveUI;
using ReactiveUI.Routing;
using ReactiveUI.Xaml;
using Newtonsoft.Json;

#if MONO
using System.Drawing;
using SolidColorBrush = System.Nullable<System.Drawing.Color>;
using MonoMac.AppKit;
using MonoMac.Foundation;
using RestSharp;
#else
using System.Net.Http;
using System.Windows.Media.Imaging;
#endif

namespace RxUI_QCon
{
    public class MainWindowViewModel : ReactiveObject, IRoutableViewModel
    {
        int _Red;
        public int Red {
            get { return _Red; }
            set { this.RaiseAndSetIfChanged(x => x.Red, value); }
        }

        int _Green;
        public int Green {
            get { return _Green; }
            set { this.RaiseAndSetIfChanged(x => x.Green, value); }
        }

        int _Blue;
        public int Blue {
            get { return _Blue; }
            set { this.RaiseAndSetIfChanged(x => x.Blue, value); }
        }

        ObservableAsPropertyHelper<SolidColorBrush> _FinalColor;
        public SolidColorBrush FinalColor {
            get { return _FinalColor.Value; }
        }

        bool _IsBusy;
        public bool IsBusy {
            get { return _IsBusy; }
            set { this.RaiseAndSetIfChanged(x => x.IsBusy, value); }
        }

#if MONO
        ObservableAsPropertyHelper<IList<NSImage>> _Images;
        public IList<NSImage> Images {
            get { return _Images.Value; }
        }
#else
        ObservableAsPropertyHelper<IList<BitmapImage>> _Images;
        public IList<BitmapImage> Images {
            get { return _Images.Value; }
        }
#endif

        public ReactiveCommand Ok { get; protected set; }

        public MainWindowViewModel(IScreen screen)
        {
            HostScreen = screen;
            var whenAnyColorChanges = this.WhenAny(x => x.Red, x => x.Green, x => x.Blue,
                    (r, g, b) => Tuple.Create(r.Value, g.Value, b.Value))
                .Select(intsToColor);

            whenAnyColorChanges
                .Where(x => x != null)
                .Select(x => new SolidColorBrush(x.Value))
                .ToProperty(this, x => x.FinalColor);

            Ok = new ReactiveCommand(whenAnyColorChanges.Select(x => x != null));

            this.WhenAny(x => x.FinalColor, x => x.Value)
                .Throttle(TimeSpan.FromSeconds(0.7), RxApp.DeferredScheduler)
                .Do(_ => IsBusy = true)
#if MONO
                .Select(x => imagesForColor(x.Value))
#else
                .Select(x => {return imagesForColor(x.Color);})
#endif
                .Switch()
                .SelectMany(imageListToImages)
                .Do(_ => IsBusy = false)
                .ToProperty(this, x => x.Images);
        }

        Color? intsToColor(Tuple<int, int, int> colorsAsInts)
        {
            byte? r = inRange(colorsAsInts.Item1), g = inRange(colorsAsInts.Item2), b = inRange(colorsAsInts.Item3);

            if (r == null || g == null || b == null) return null;
#if MONO
            return Color.FromArgb(r.Value, g.Value, b.Value);
#else
            return Color.FromRgb(r.Value, g.Value, b.Value);
#endif
        }

        static byte? inRange(int value)
        {
            if (value < 0 || value > 255) {
                return null;
            }

            return (byte) value;
        }

#if MONO
        IObservable<ImageList> imagesForColor(Color sourceColor)
        {
            var queryParams = new[] {
                new { k = "method", v = "flickr_color_search" },
                new { k = "limit", v = "73" },
                new { k = "offset", v = "0" },
                new { k = "colors[0]", v = String.Format("{0:x2}{1:x2}{2:x2}", sourceColor.R, sourceColor.G, sourceColor.B) },
                new { k = "weights[0]", v = "1" },
            };

            var client = new RestClient("http://labs.tineye.com");
            var rq = new RestRequest("rest//");
            foreach(var p in queryParams) {
                rq.AddParameter(p.k, p.v, ParameterType.GetOrPost);
            }

            return Observable.Start(() => {
                var resp = client.Execute(rq);
                return JsonConvert.DeserializeObject<ImageList>(resp.Content);
            }, RxApp.TaskpoolScheduler);
        }

        IObservable<IList<NSImage>> imageListToImages(ImageList imageList)
        {
            return imageList.result.ToObservable()
                .Select(x => "http://img.tineye.com/flickr-images/?filepath=labs-flickr/" + x.filepath)
                .Select(x => Observable.Start(() => NSData.FromUrl(new NSUrl(x)))).Merge(4)
                .ObserveOn(RxApp.DeferredScheduler)
                .Select(x => new NSImage(x))
                .ToList();
        }
#else
        IObservable<ImageList> imagesForColor(Color sourceColor)
        {
            var queryParams = new[] {
                new { k = "method", v = "flickr_color_search" },
                new { k = "limit", v = "73" },
                new { k = "offset", v = "0" },
                new { k = "colors[0]", v = String.Format("{0:x2}{1:x2}{2:x2}", sourceColor.R, sourceColor.G, sourceColor.B) },
                new { k = "weights[0]", v = "1" },
            };

            var query = queryParams.Aggregate("",
                (acc, x) => String.Format("{0}&{1}={2}", acc, HttpUtility.UrlEncode(x.k), HttpUtility.UrlEncode(x.v)));

            query = query.Substring(1);

            var wc = new HttpClient();
            var url = "http://labs.tineye.com/rest/?" + query;
            wc.BaseAddress = new Uri(url);

            return wc.GetStringAsync("").ToObservable()
                .Select(JsonConvert.DeserializeObject<ImageList>);
        }

        IObservable<IList<BitmapImage>> imageListToImages(ImageList imageList)
        {
            return imageList.result.ToObservable(RxApp.DeferredScheduler)
                .Select(x => "http://img.tineye.com/flickr-images/?filepath=labs-flickr/" + x.filepath)
                .Select(x => {
                    var ret = new BitmapImage(new Uri(x));
                    return ret;
                }).ToList();
        }
#endif
        public string UrlPathSegment { get; private set; }
        public IScreen HostScreen { get; private set; }
    }
}