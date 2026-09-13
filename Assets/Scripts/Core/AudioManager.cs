using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [Header("Audio Sources")]
    public AudioSource bgmSource;
    public AudioSource sfxSource;

    private AudioClip defaultBGM;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (bgmSource != null)
            defaultBGM = bgmSource.clip;

        LoadVolumes();
    }

    void Start()
    {
        if (bgmSource != null && bgmSource.clip != null && !bgmSource.isPlaying)
            bgmSource.Play();
    }

    public void PlayDefaultBGM()
    {
        PlayBGM(defaultBGM);
    }

    void LoadVolumes()
    {
        float master,
            bgm,
            sfx;

        if (SaveManager.Instance != null)
        {
            var data = SaveManager.Instance.Data;
            master = data.volumeMaster;
            bgm = data.volumeBGM;
            sfx = data.volumeSFX;
        }
        else
        {
            master = PlayerPrefs.GetFloat("Vol_Master", 1f);
            bgm = PlayerPrefs.GetFloat("Vol_BGM", 1f);
            sfx = PlayerPrefs.GetFloat("Vol_SFX", 1f);
        }

        AudioListener.volume = master;
        SetBGMVolume(bgm);
        SetSFXVolume(sfx);
    }

    public void SetBGMVolume(float value)
    {
        if (bgmSource != null)
            bgmSource.volume = value;
    }

    public void SetSFXVolume(float value)
    {
        if (sfxSource != null)
            sfxSource.volume = value;
    }

    public void PlayBGM(AudioClip clip)
    {
        if (bgmSource == null)
            return;
        bgmSource.Stop();
        bgmSource.clip = clip;
        if (clip != null)
            bgmSource.Play();
    }

    public void PlaySFX(AudioClip clip)
    {
        if (sfxSource != null && clip != null)
            sfxSource.PlayOneShot(clip);
    }
}
