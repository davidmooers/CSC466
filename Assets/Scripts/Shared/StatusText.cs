using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Text))]
public class StatusText : MonoBehaviour
{

    private TMP_Text text;

    private void Awake()
    {
        text = GetComponent<TMP_Text>();
    }

    public void SetText(string message)
    {
        text.text = message;
    }

}
