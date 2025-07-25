﻿using Microsoft.FSharp.Core;
using BaboonAPI.Hooks.Tracks;
using UnityEngine;
using BepInEx;
using System.IO;
using Microsoft.FSharp.Collections;
using TootTallyCore.Graphics;
using TootTallyCore.APIServices;
using TootTallyCore.Utils.Assets;
using TootTallyCore.Utils.Helpers;
using TootTallyCore.Utils.TootTallyNotifs;
using UnityEngine.UI;
using System;

namespace TootTallyTwitchIntegration
{
    public class RequestPanelRow
    {
        private string _downloadLink;
        private GameObject _requestRowContainer;
        private GameObject _requestRow;
        private GameObject _downloadButton;
        private ProgressBar _progressBar;
        private TracksLoaderListener _reloadListener;
        public Plugin.Request request { get; private set; }
        private SerializableClass.SongDataFromDB _chart;

        public RequestPanelRow(Transform canvasTransform, Plugin.Request request)
        {
            _chart = request.songData;
            this.request = request;
            _requestRow = GameObject.Instantiate(RequestPanelManager.requestRowPrefab, canvasTransform);
            _requestRow.name = $"Request{_chart.name}";
            _requestRowContainer = _requestRow.transform.Find("LatencyFG/MainPage").gameObject;
            _requestRow.transform.Find("LatencyFG").GetComponent<Image>().color = new Color(.05f, .05f, .05f);
            var songNameText = GameObjectFactory.CreateSingleText(_requestRowContainer.transform, "SongName", _chart.name);
            var charterText = GameObjectFactory.CreateSingleText(_requestRowContainer.transform, "Charter", _chart.charter ?? "Unknown");
            var requesterText = GameObjectFactory.CreateSingleText(_requestRowContainer.transform, "RequestedByName", request.requester);
            var time = TimeSpan.FromSeconds(request.songData.song_length);
            var stringTime = time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time:mm\\:ss}" : $"{time:m\\:ss}";
            var songLengthText = GameObjectFactory.CreateSingleText(_requestRowContainer.transform, "SongLength", stringTime);
            var requestTime = GameObjectFactory.CreateSingleText(_requestRowContainer.transform, "Time", request.date);
            //fuck that shit :skull:
            songNameText.GetComponent<RectTransform>().sizeDelta = new Vector2(250, 64);
            songLengthText.GetComponent<RectTransform>().sizeDelta = new Vector2(120, 64);
            charterText.GetComponent<RectTransform>().sizeDelta = requesterText.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 64);
            requestTime.GetComponent<RectTransform>().sizeDelta = new Vector2(150, 64);
            songNameText.overflowMode = charterText.overflowMode = requesterText.overflowMode = requestTime.overflowMode = TMPro.TextOverflowModes.Ellipsis;

            if (FSharpOption<TromboneTrack>.get_IsNone(TrackLookup.tryLookup(_chart.track_ref)))
            {
                _downloadLink = FileHelper.GetDownloadLinkFromSongData(_chart);
                if (_downloadLink != null)
                {
                    _downloadButton = GameObjectFactory.CreateCustomButton(_requestRowContainer.transform, Vector2.zero, new Vector2(68, 68), AssetManager.GetSprite("Download64.png"), "DownloadButton", DownloadChart).gameObject;
                    _progressBar = GameObjectFactory.CreateProgressBar(_requestRow.transform.Find("LatencyFG"), Vector2.zero, new Vector2(900, 20), false, "ProgressBar");
                }
                else
                    _downloadButton = GameObjectFactory.CreateCustomButton(_requestRowContainer.transform, Vector2.zero, new Vector2(68, 68), AssetManager.GetSprite("global64.png"), "OpenLeaderboardButton", () => Application.OpenURL($"https://toottally.com/song/{request.songData.id}/")).gameObject;

            }
            else
                GameObjectFactory.CreateCustomButton(_requestRowContainer.transform, Vector2.zero, new Vector2(68, 68), AssetManager.GetSprite("Check64.png"), "PlayButton", PlayChart);

            GameObjectFactory.CreateCustomButton(_requestRowContainer.transform, Vector2.zero, new Vector2(68, 68), AssetManager.GetSprite("Close64.png"), "SkipButton", RemoveFromPanel);
            GameObjectFactory.CreateCustomButton(_requestRowContainer.transform, Vector2.zero, new Vector2(68, 68), AssetManager.GetSprite("Block64.png"), "BlockButton", BlockChart);
            _requestRow.SetActive(true);
        }

        public void PlayChart()
        {
            RequestPanelManager.currentSongID = request.song_id;
            RequestPanelManager.SetTrackToTrackref(_chart.track_ref);
        }

        public void DownloadChart()
        {
            _downloadButton.SetActive(false);
            Plugin.Instance.StartCoroutine(TootTallyAPIService.DownloadZipFromServer(_downloadLink, _progressBar, data =>
            {
                if (data != null)
                {
                    string downloadDir = Path.Combine(Path.GetDirectoryName(Plugin.Instance.Info.Location), "Downloads/");

                    if (!Directory.Exists(downloadDir)) Directory.CreateDirectory(downloadDir);

                    string fileName = $"{_chart.id}.zip";
                    FileHelper.WriteBytesToFile(downloadDir, fileName, data);

                    string source = Path.Combine(downloadDir, fileName);
                    string destination = Path.Combine(Paths.BepInExRootPath, "CustomSongs/");
                    FileHelper.ExtractZipToDirectory(source, destination);

                    FileHelper.DeleteFile(downloadDir, fileName);

                    _reloadListener = new TracksLoaderListener(this);
                    TracksLoadedEvent.EVENT.Register(_reloadListener);

                    TootTallyNotifManager.DisplayNotif("Reloading Songs...");
                    TootTallyCore.Plugin.Instance.reloadManager?.ReloadAll(new ProgressCallbacks()
                    {
                        onComplete = () =>
                        {
                            TootTallyNotifManager.DisplayNotif("Reload complete!");
                            var play = GameObjectFactory.CreateCustomButton(_requestRowContainer.transform, Vector2.zero, new Vector2(68, 68), AssetManager.GetSprite("Check64.png"), "PlayButton", PlayChart);
                            play.transform.SetSiblingIndex(5);
                        },
                        onError = (err) =>
                        {
                            TootTallyNotifManager.DisplayNotif($"Reloading failed! {err.Message}");
                        }
                    });
                }
                else
                {
                    TootTallyNotifManager.DisplayNotif("Download not available.");
                    _downloadButton.SetActive(true);
                }
            }));
        }

        public class TracksLoaderListener : TracksLoadedEvent.Listener
        {
            private RequestPanelRow _row;
            public TracksLoaderListener(RequestPanelRow row)
            {
                _row = row;
            }

            public void OnTracksLoaded(FSharpList<TromboneTrack> value)
            {
                _row.PlayChart();
                _row.UnsubscribeLoaderEvent();
            }
        }

        public void UnsubscribeLoaderEvent()
        {
            if (_reloadListener != null)
                TracksLoadedEvent.EVENT.Unregister(_reloadListener);
            _reloadListener = null;
        }

        public void RemoveFromPanel()
        {
            RequestPanelManager.Remove(this);
            GameObject.DestroyImmediate(_requestRow);
        }

        public void BlockChart()
        {
            RequestPanelManager.AddToBlockList(_chart.id);
            RemoveFromPanel();
        }
    }
}
