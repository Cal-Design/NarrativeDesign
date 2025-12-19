using UnityEngine;

public class InteractionRaycast : MonoBehaviour
{
    [Header("Raycast")]
    public float interactionDistance = 10f;
    public LayerMask interactableLayer;

    private Outline currentOutline;

    void Update()
    {
        Ray ray = new Ray(transform.position, transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, interactionDistance, interactableLayer))
        {
            Outline outline = hit.collider.GetComponent<Outline>();

            if (outline != null)
            {
                if (currentOutline != outline)
                {
                    DisableCurrentOutline();
                    currentOutline = outline;
                    currentOutline.enabled = true;
                }
            }
        }
        else
        {
            DisableCurrentOutline();
        }
    }

    void DisableCurrentOutline()
    {
        if (currentOutline != null)
        {
            currentOutline.enabled = false;
            currentOutline = null;
        }
    }
}