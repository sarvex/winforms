// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using static System.Windows.Forms.Layout.DefaultLayout;

namespace System.Windows.Forms.Layout
{
    internal partial class AnchorLayout : LayoutEngine
    {
        internal static readonly AnchorLayout s_instance = new();

        //private static readonly int s_layoutInfoProperty = PropertyStore.CreateKey();
        private static readonly int s_cachedBoundsProperty = PropertyStore.CreateKey();

        /// <summary>
        ///  Loop through the AutoSized controls and expand them if they are smaller than
        ///  their preferred size. If expanding the controls causes overlap, bump the overlapped
        ///  control if it is AutoRelocatable.
        /// </summary>
        private static void LayoutAutoSizedControls(IArrangedElement container)
        {
            ArrangedElementCollection children = container.Children;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                IArrangedElement element = children[i];
                if (CommonProperties.xGetAutoSizedAndAnchored(element))
                {
                    Rectangle bounds = GetCachedBounds(element);

                    AnchorStyles anchor = GetAnchor(element);
                    Size proposedConstraints = LayoutUtils.s_maxSize;

                    if ((anchor & (AnchorStyles.Left | AnchorStyles.Right)) == (AnchorStyles.Left | AnchorStyles.Right))
                    {
                        proposedConstraints.Width = bounds.Width;
                    }

                    if ((anchor & (AnchorStyles.Top | AnchorStyles.Bottom)) == (AnchorStyles.Top | AnchorStyles.Bottom))
                    {
                        proposedConstraints.Height = bounds.Height;
                    }

                    Size prefSize = element.GetPreferredSize(proposedConstraints);
                    Rectangle newBounds = bounds;
                    if (CommonProperties.GetAutoSizeMode(element) == AutoSizeMode.GrowAndShrink)
                    {
                        // this is the case for simple things like radio button, checkbox, etc.
                        newBounds = GetGrowthBounds(element, prefSize);
                    }
                    else
                    {
                        // we had whacked this check, but it turns out it causes undesirable
                        // behavior in things like panel. a panel with no elements sizes to 0,0.
                        if (bounds.Width < prefSize.Width || bounds.Height < prefSize.Height)
                        {
                            Size newSize = LayoutUtils.UnionSizes(bounds.Size, prefSize);
                            newBounds = GetGrowthBounds(element, newSize);
                        }
                    }

                    if (newBounds != bounds)
                    {
                        SetCachedBounds(element, newBounds);
                    }
                }
            }
        }

        /// <summary>
        ///  Gets the bounds of the element after growing to newSize (note that depending on
        ///  anchoring the element may grow to the left/upwards rather than to the
        ///  right/downwards. i.e., it may be translated.)
        /// </summary>
        private static Rectangle GetGrowthBounds(IArrangedElement element, Size newSize)
        {
            GrowthDirection direction = GetGrowthDirection(element);
            Rectangle oldBounds = GetCachedBounds(element);
            Point location = oldBounds.Location;

            Debug.Assert((CommonProperties.GetAutoSizeMode(element) == AutoSizeMode.GrowAndShrink || (newSize.Height >= oldBounds.Height && newSize.Width >= oldBounds.Width)),
                "newSize expected to be >= current size.");

            if ((direction & GrowthDirection.Left) != GrowthDirection.None)
            {
                // We are growing towards the left, translate X
                location.X -= newSize.Width - oldBounds.Width;
            }

            if ((direction & GrowthDirection.Upward) != GrowthDirection.None)
            {
                // We are growing towards the top, translate Y
                location.Y -= newSize.Height - oldBounds.Height;
            }

            Rectangle newBounds = new Rectangle(location, newSize);

            Debug.Assert((CommonProperties.GetAutoSizeMode(element) == AutoSizeMode.GrowAndShrink || newBounds.Contains(oldBounds)), "How did we resize in such a way we no longer contain our old bounds?");

            return newBounds;
        }

        /// <summary>
        ///  Examines an elements anchoring to figure out which direction it should grow.
        /// </summary>
        private static GrowthDirection GetGrowthDirection(IArrangedElement element)
        {
            AnchorStyles anchor = GetAnchor(element);
            GrowthDirection growthDirection = GrowthDirection.None;

            if ((anchor & AnchorStyles.Right) != AnchorStyles.None
                && (anchor & AnchorStyles.Left) == AnchorStyles.None)
            {
                // element is anchored to the right, but not the left.
                growthDirection |= GrowthDirection.Left;
            }
            else
            {
                // otherwise we grow towards the right (common case)
                growthDirection |= GrowthDirection.Right;
            }

            if ((anchor & AnchorStyles.Bottom) != AnchorStyles.None
                && (anchor & AnchorStyles.Top) == AnchorStyles.None)
            {
                // element is anchored to the bottom, but not the top.
                growthDirection |= GrowthDirection.Upward;
            }
            else
            {
                // otherwise we grow towards the bottom. (common case)
                growthDirection |= GrowthDirection.Downward;
            }

            Debug.Assert((growthDirection & GrowthDirection.Left) == GrowthDirection.None
                || (growthDirection & GrowthDirection.Right) == GrowthDirection.None,
                "We shouldn't allow growth to both the left and right.");
            Debug.Assert((growthDirection & GrowthDirection.Upward) == GrowthDirection.None
                || (growthDirection & GrowthDirection.Downward) == GrowthDirection.None,
                "We shouldn't allow both upward and downward growth.");
            return growthDirection;
        }

        /// <summary>
        ///  Layout for a single anchored control. There's no order dependency when laying out anchored controls.
        /// </summary>
        private static Rectangle GetAnchorDestination(IArrangedElement element, Rectangle displayRect, bool measureOnly)
        {
            // Container can not be null since we AnchorControls takes a non-null container.
            //
            // NB: DO NOT convert the following into Debug.WriteLineIf(CompModSwitches.RichLayout.TraceInfo, "...")
            // because it WILL execute GetCachedBounds(element).ToString() calls even if CompModSwitches.RichLayout.TraceInfo=false
            // This in turn will lead to a cascade of native calls and callbacks
            if (CompModSwitches.RichLayout.TraceInfo)
            {
                Debug.WriteLine($"\t\t'{element}' is anchored at {GetCachedBounds(element)}");
            }

            if(element is not Control control)
            {
                Debug.WriteLine($"\t\t'{element}' is not control");
                return element.Bounds;
            }

            int? x = control.Anchors!.Left;
            x ??= control.Bounds.Left;
            int width = control.Width;
            int? y = control.Anchors!.Top;

            y ??= control.Bounds.Top;
            int height = control.Height;

            if (x < 0 || width < 0 || y < 0 || height < 0)
            {
                Debug.WriteLine($"\t\t'{element}' anchors resulted in negative bounds");
            }

            if (control.Anchors.Left is not null)
            {
                if (control.Anchors.Right is not null)
                {
                    width = displayRect.Width - (control.Anchors.Right.Value + x.Value);
                }
            }
            else
            if (control.Anchors.Right is not null)
            {
                x = displayRect.Width - control.Width - control.Anchors.Right.Value;
                if (x < 0)
                {
                    x =0;
                }
            }

            if (control.Anchors.Top is not null)
            {
                if (control.Anchors.Bottom is not null)
                {
                    height = displayRect.Height - (control.Anchors.Bottom.Value + y.Value);
                }
            }
            else if (control.Anchors.Bottom is not null)
            {
                y = displayRect.Height - control.Height - control.Anchors.Bottom.Value;
                if (y < 0)
                {
                    y = 0;
                }
            }

/*
            if (!measureOnly)
            {
                // the size is actually zero, set the width and heights appropriately.
                if (width < 0)
                {
                    width = 0;
                }

                if (height < 0)
                {
                    height = 0;
                }
            }
            else
            {
                Rectangle cachedBounds = GetCachedBounds(element);
                // in this scenario we've likely been passed a 0 sized display rectangle to determine our height.
                // we will need to translate the right and bottom edges as necessary to the positive plane.

                // right < left means the control is anchored both left and right.
                // cachedBounds != element.Bounds means  the element's size has changed
                // any, all, or none of these can be true.
                if (right < left || cachedBounds.Width != element.Bounds.Width || cachedBounds.X != element.Bounds.X)
                {
                    if (cachedBounds != element.Bounds)
                    {
                        left = Math.Max(Math.Abs(left), Math.Abs(cachedBounds.Left));
                    }

                    right = left + Math.Max(element.Bounds.Width, cachedBounds.Width) + Math.Abs(right);
                }
                else
                {
                    left = left > 0 ? left : element.Bounds.Left;
                    right = right > 0 ? right : element.Bounds.Right + Math.Abs(right);
                }

                // bottom < top means the control is anchored both top and bottom.
                // cachedBounds != element.Bounds means  the element's size has changed
                // any, all, or none of these can be true.
                if (bottom < top || cachedBounds.Height != element.Bounds.Height || cachedBounds.Y != element.Bounds.Y)
                {
                    if (cachedBounds != element.Bounds)
                    {
                        top = Math.Max(Math.Abs(top), Math.Abs(cachedBounds.Top));
                    }

                    bottom = top + Math.Max(element.Bounds.Height, cachedBounds.Height) + Math.Abs(bottom);
                }
                else
                {
                    top = top > 0 ? top : element.Bounds.Top;
                    bottom = bottom > 0 ? bottom : element.Bounds.Bottom + Math.Abs(bottom);
                }
            }

            Debug.WriteLineIf(CompModSwitches.RichLayout.TraceInfo, "\t\t...new anchor dim (l,t,r,b) {"
                                                                      + (left)
                                                                      + ", " + (top)
                                                                      + ", " + (right)
                                                                      + ", " + (bottom)
                                                                      + "}");
*/
            return new Rectangle(x!.Value, y!.Value, width, height);
        }

        private static void LayoutAnchoredControls(IArrangedElement container)
        {
            Debug.WriteLineIf(CompModSwitches.RichLayout.TraceInfo, "\tAnchor Processing");
            Debug.WriteLineIf(CompModSwitches.RichLayout.TraceInfo, "\t\tdisplayRect: " + container.DisplayRectangle.ToString());

            Rectangle displayRectangle = container.DisplayRectangle;
            if (CommonProperties.GetAutoSize(container) && ((displayRectangle.Width == 0) || (displayRectangle.Height == 0)))
            {
                // we haven't set ourselves to the preferred size yet. proceeding will
                // just set all the control widths to zero. let's return here
                return;
            }

            ArrangedElementCollection children = container.Children;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                IArrangedElement element = children[i];
                if (CommonProperties.GetNeedsAnchorLayout(element))
                {
                    //Debug.Assert(GetAnchorInfo(element) is not null, "AnchorInfo should be initialized before LayoutAnchorControls().");
                    SetCachedBounds(element, GetAnchorDestination(element, displayRectangle, /*measureOnly=*/false));
                }
            }
        }

        private static Size LayoutDockedControls(IArrangedElement container, bool measureOnly)
        {
            Debug.WriteLineIf(CompModSwitches.RichLayout.TraceInfo, "\tDock Processing");
            Debug.Assert(!HasCachedBounds(container), "Do not call this method with an active cached bounds list.");

            // If measuring, we start with an empty rectangle and add as needed.
            // If doing actual layout, we start with the container's rect and subtract as we layout.
            Rectangle remainingBounds = measureOnly ? Rectangle.Empty : container.DisplayRectangle;
            Size preferredSize = Size.Empty;

            IArrangedElement? mdiClient = null;

            // Docking layout is order dependent. After much debate, we decided to use z-order as the
            // docking order. (Introducing a DockOrder property was a close second)
            ArrangedElementCollection children = container.Children;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                IArrangedElement element = children[i];
                Debug.Assert(element.Bounds == GetCachedBounds(element), "Why do we have cachedBounds for a docked element?");
                if (CommonProperties.GetNeedsDockLayout(element))
                {
                    // Some controls modify their bounds when you call SetBoundsCore. We
                    // therefore need to read the value of bounds back when adjusting our layout rectangle.
                    switch (GetDock(element))
                    {
                        case DockStyle.Top:
                            {
                                Size elementSize = GetVerticalDockedSize(element, remainingBounds.Size, measureOnly);
                                Rectangle newElementBounds = new Rectangle(remainingBounds.X, remainingBounds.Y, elementSize.Width, elementSize.Height);

                                TryCalculatePreferredSizeDockedControl(element, newElementBounds, measureOnly, ref preferredSize, ref remainingBounds);

                                // What we are really doing here: top += element.Bounds.Height;
                                remainingBounds.Y += element.Bounds.Height;
                                remainingBounds.Height -= element.Bounds.Height;
                                break;
                            }

                        case DockStyle.Bottom:
                            {
                                Size elementSize = GetVerticalDockedSize(element, remainingBounds.Size, measureOnly);
                                Rectangle newElementBounds = new Rectangle(remainingBounds.X, remainingBounds.Bottom - elementSize.Height, elementSize.Width, elementSize.Height);

                                TryCalculatePreferredSizeDockedControl(element, newElementBounds, measureOnly, ref preferredSize, ref remainingBounds);

                                // What we are really doing here: bottom -= element.Bounds.Height;
                                remainingBounds.Height -= element.Bounds.Height;

                                break;
                            }

                        case DockStyle.Left:
                            {
                                Size elementSize = GetHorizontalDockedSize(element, remainingBounds.Size, measureOnly);
                                Rectangle newElementBounds = new Rectangle(remainingBounds.X, remainingBounds.Y, elementSize.Width, elementSize.Height);

                                TryCalculatePreferredSizeDockedControl(element, newElementBounds, measureOnly, ref preferredSize, ref remainingBounds);

                                // What we are really doing here: left += element.Bounds.Width;
                                remainingBounds.X += element.Bounds.Width;
                                remainingBounds.Width -= element.Bounds.Width;
                                break;
                            }

                        case DockStyle.Right:
                            {
                                Size elementSize = GetHorizontalDockedSize(element, remainingBounds.Size, measureOnly);
                                Rectangle newElementBounds = new Rectangle(remainingBounds.Right - elementSize.Width, remainingBounds.Y, elementSize.Width, elementSize.Height);

                                TryCalculatePreferredSizeDockedControl(element, newElementBounds, measureOnly, ref preferredSize, ref remainingBounds);

                                // What we are really doing here: right -= element.Bounds.Width;
                                remainingBounds.Width -= element.Bounds.Width;
                                break;
                            }

                        case DockStyle.Fill:
                            if (element is MdiClient)
                            {
                                Debug.Assert(mdiClient is null, "How did we end up with multiple MdiClients?");
                                mdiClient = element;
                            }
                            else
                            {
                                Size elementSize = remainingBounds.Size;
                                Rectangle newElementBounds = new Rectangle(remainingBounds.X, remainingBounds.Y, elementSize.Width, elementSize.Height);

                                TryCalculatePreferredSizeDockedControl(element, newElementBounds, measureOnly, ref preferredSize, ref remainingBounds);
                            }

                            break;
                        default:
                            Debug.Fail("Unsupported value for dock.");
                            break;
                    }
                }

                // Treat the MDI client specially, since it's supposed to blend in with the parent form
                if (mdiClient is not null)
                {
                    SetCachedBounds(mdiClient, remainingBounds);
                }
            }

            return preferredSize;
        }

        /// <summary>
        ///  Helper method that either sets the element bounds or does the preferredSize computation based on
        ///  the value of measureOnly.
        /// </summary>
        private static void TryCalculatePreferredSizeDockedControl(IArrangedElement element, Rectangle newElementBounds, bool measureOnly, ref Size preferredSize, ref Rectangle remainingBounds)
        {
            if (measureOnly)
            {
                Size neededSize = new Size(
                    Math.Max(0, newElementBounds.Width - remainingBounds.Width),
                    Math.Max(0, newElementBounds.Height - remainingBounds.Height));

                DockStyle dockStyle = GetDock(element);
                if ((dockStyle == DockStyle.Top) || (dockStyle == DockStyle.Bottom))
                {
                    neededSize.Width = 0;
                }

                if ((dockStyle == DockStyle.Left) || (dockStyle == DockStyle.Right))
                {
                    neededSize.Height = 0;
                }

                if (dockStyle != DockStyle.Fill)
                {
                    preferredSize += neededSize;
                    remainingBounds.Size += neededSize;
                }
                else if (dockStyle == DockStyle.Fill && CommonProperties.GetAutoSize(element))
                {
                    Size elementPrefSize = element.GetPreferredSize(neededSize);
                    remainingBounds.Size += elementPrefSize;
                    preferredSize += elementPrefSize;
                }
            }
            else
            {
                element.SetBounds(newElementBounds, BoundsSpecified.None);

#if DEBUG
                Control control = (Control)element;
                newElementBounds.Size = control!.ApplySizeConstraints(newElementBounds.Size);

                // This usually happens when a Control overrides its SetBoundsCore or sets size during OnResize
                // to enforce constraints like AutoSize. Generally you can just move this code to Control.GetAdjustedSize
                // and then PreferredSize will also pick up these constraints. See ComboBox as an example.
                if (CommonProperties.GetAutoSize(element) && !CommonProperties.GetSelfAutoSizeInDefaultLayout(element))
                {
                    Debug.Assert(
                        (newElementBounds.Width < 0 || element.Bounds.Width == newElementBounds.Width) &&
                        (newElementBounds.Height < 0 || element.Bounds.Height == newElementBounds.Height),
                        "Element modified its bounds during docking -- PreferredSize will be wrong. See comment near this assert.");
                }
#endif
            }
        }

        private static Size GetVerticalDockedSize(IArrangedElement element, Size remainingSize, bool measureOnly)
        {
            Size newSize = xGetDockedSize(element, /* constraints = */ new Size(remainingSize.Width, 1));
            if (!measureOnly)
            {
                newSize.Width = remainingSize.Width;
            }
            else
            {
                newSize.Width = Math.Max(newSize.Width, remainingSize.Width);
            }

            Debug.Assert((measureOnly && (newSize.Width >= remainingSize.Width)) || (newSize.Width == remainingSize.Width),
                "Error detected in GetVerticalDockedSize: Dock size computed incorrectly during layout.");
            return newSize;
        }

        private static Size GetHorizontalDockedSize(IArrangedElement element, Size remainingSize, bool measureOnly)
        {
            Size newSize = xGetDockedSize(element, /* constraints = */ new Size(1, remainingSize.Height));
            if (!measureOnly)
            {
                newSize.Height = remainingSize.Height;
            }
            else
            {
                newSize.Height = Math.Max(newSize.Height, remainingSize.Height);
            }

            Debug.Assert((measureOnly && (newSize.Height >= remainingSize.Height)) || (newSize.Height == remainingSize.Height),
                "Error detected in GetHorizontalDockedSize: Dock size computed incorrectly during layout.");
            return newSize;
        }

        private static Size xGetDockedSize(IArrangedElement element, Size constraints)
        {
            Size desiredSize;
            if (CommonProperties.GetAutoSize(element))
            {
                // Ask control for its desired size using the provided constraints.
                // (e.g., a control docked to top will constrain width to remaining width
                // and minimize height.)
                desiredSize = element.GetPreferredSize(constraints);
            }
            else
            {
                desiredSize = element.Bounds.Size;
            }

            Debug.Assert((desiredSize.Width >= 0 && desiredSize.Height >= 0), "Error detected in xGetDockSize: Element size was negative.");
            return desiredSize;
        }

        private protected override bool LayoutCore(IArrangedElement container, LayoutEventArgs args)
        {
            return TryCalculatePreferredSize(container, measureOnly: false, preferredSize: out Size _);
        }

        /// <remarks>
        ///  PreferredSize is only computed if measureOnly = true.
        /// </remarks>
        private static bool TryCalculatePreferredSize(IArrangedElement container, bool measureOnly, out Size preferredSize)
        {
            ArrangedElementCollection children = container.Children;
            // PreferredSize is garbage unless measureOnly is specified
            preferredSize = new Size(-7103, -7105);

            // Short circuit for items with no children
            if (!measureOnly && children.Count == 0)
            {
                return CommonProperties.GetAutoSize(container);
            }

            bool dock = false;
            bool anchor = false;
            bool autoSize = false;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                IArrangedElement element = children[i];
                if (CommonProperties.GetNeedsDockAndAnchorLayout(element))
                {
                    if (!dock && CommonProperties.GetNeedsDockLayout(element))
                    {
                        dock = true;
                    }

                    if (!anchor && CommonProperties.GetNeedsAnchorLayout(element))
                    {
                        anchor = true;
                    }

                    if (!autoSize && CommonProperties.xGetAutoSizedAndAnchored(element))
                    {
                        autoSize = true;
                    }
                }
            }

            Debug.WriteLineIf(CompModSwitches.RichLayout.TraceInfo, "\tanchor : " + anchor.ToString());
            Debug.WriteLineIf(CompModSwitches.RichLayout.TraceInfo, "\tdock :   " + dock.ToString());

            Size preferredSizeForDocking = Size.Empty;
            Size preferredSizeForAnchoring = Size.Empty;

            if (dock)
            {
                preferredSizeForDocking = LayoutDockedControls(container, measureOnly);
            }

            if (anchor && !measureOnly)
            {
                // In the case of anchor, where we currently are defines the preferred size,
                // so don't recalculate the positions of everything.
                LayoutAnchoredControls(container);
            }

            if (autoSize)
            {
                LayoutAutoSizedControls(container);
            }

            if (!measureOnly)
            {
                // Set the anchored controls to their computed positions.
                ApplyCachedBounds(container);
            }
            else
            {
                // Finish the preferredSize computation and clear cached anchored positions.
                preferredSizeForAnchoring = GetAnchorPreferredSize(container);

                Padding containerPadding = Padding.Empty;
                if (container is Control control)
                {
                    // Calling this will respect Control.DefaultPadding.
                    containerPadding = control.Padding;
                }
                else
                {
                    // Not likely to happen but handle this gracefully.
                    containerPadding = CommonProperties.GetPadding(container, Padding.Empty);
                }

                preferredSizeForAnchoring.Width -= containerPadding.Left;
                preferredSizeForAnchoring.Height -= containerPadding.Top;

                ClearCachedBounds(container);
                preferredSize = LayoutUtils.UnionSizes(preferredSizeForDocking, preferredSizeForAnchoring);
            }

            return CommonProperties.GetAutoSize(container);
        }

        public static AnchorStyles GetAnchor(IArrangedElement element) => CommonProperties.xGetAnchor(element);

        public static DockStyle GetDock(IArrangedElement element) => CommonProperties.xGetDock(element);

        /*public static void SetDock(IArrangedElement element, DockStyle value)
        {
            Debug.Assert(!HasCachedBounds(element.Container), "Do not call this method with an active cached bounds list.");

            if (GetDock(element) != value)
            {
                SourceGenerated.EnumValidator.Validate(value);

                bool dockNeedsLayout = CommonProperties.GetNeedsDockLayout(element);
                CommonProperties.xSetDock(element, value);

                using (new LayoutTransaction(element.Container as Control, element, PropertyNames.Dock))
                {
                    // if the item is autosized, calling setbounds performs a layout, which
                    // if we haven't set the anchor info properly yet makes dock/anchor layout cranky.
                    if (value == DockStyle.None)
                    {
                        if (dockNeedsLayout)
                        {
                            // We are transitioning from docked to not docked, restore the original bounds.
                            element.SetBounds(CommonProperties.GetSpecifiedBounds(element), BoundsSpecified.None);

                            // Updating AnchorInfo is only needed when control is ready for layout. InitLayoutCore() does call UpdateAnchorInfo().
                            // At the least, we are checking if control is parented before updating AnchorInfo. This helps avoid calculating
                            // AnchorInfo with default initial values of the Control. They are always overriden when layout happen.
                            if (element is Control control && control.Parent is not null)
                            {
                                // Restore Anchor information as its now relevant again.
                                UpdateAnchorInfo(element);
                            }
                        }
                    }
                    else
                    {
                        // Now setup the new bounds.
                        element.SetBounds(CommonProperties.GetSpecifiedBounds(element), BoundsSpecified.All);
                    }
                }
            }

            Debug.Assert(GetDock(element) == value, "Error setting Dock value.");
        }*/

        public static void ScaleAnchorInfo(IArrangedElement element, SizeF factor)
        {
            ControlAnchors? anchorInfo = ((Control)element).Anchors;

            // some controls don't have AnchorInfo, i.e. Panels
            if (anchorInfo is not null)
            {
                anchorInfo.Left = anchorInfo.Left == null ? null :(int)((float)anchorInfo.Left * factor.Width);
                anchorInfo.Top = anchorInfo.Top == null ? null : (int)((float)anchorInfo.Top * factor.Height);
                anchorInfo.Right = anchorInfo.Right == null ? null : (int)((float)anchorInfo.Right * factor.Width);
                anchorInfo.Bottom = anchorInfo.Bottom == null ? null : (int)((float)anchorInfo.Bottom * factor.Height);

                ((Control)element).Anchors = anchorInfo;
            }
        }

        private static Rectangle GetCachedBounds(IArrangedElement element)
        {
            if (element.Container is not null)
            {
                IDictionary? dictionary = element.Container.Properties.GetObject(s_cachedBoundsProperty) as IDictionary;
                if (dictionary is not null)
                {
                    object? bounds = dictionary[element];
                    if (bounds is not null)
                    {
                        return (Rectangle)bounds;
                    }
                }
            }

            return element.Bounds;
        }

        private static bool HasCachedBounds(IArrangedElement container)
        {
            return container is not null && container.Properties.GetObject(s_cachedBoundsProperty) is not null;
        }

        private static void ApplyCachedBounds(IArrangedElement container)
        {
            if (CommonProperties.GetAutoSize(container))
            {
                // Avoiding calling DisplayRectangle before checking AutoSize for Everett compat
                Rectangle displayRectangle = container.DisplayRectangle;
                if ((displayRectangle.Width == 0) || (displayRectangle.Height == 0))
                {
                    ClearCachedBounds(container);
                    return;
                }
            }

            IDictionary? dictionary = container.Properties.GetObject(s_cachedBoundsProperty) as IDictionary;
            if (dictionary is not null)
            {
#if DEBUG
                // In debug builds, we need to modify the collection, so we add a break and an
                // outer loop to prevent attempting to IEnumerator.MoveNext() on a modified
                // collection.
                while (dictionary.Count > 0)
                {
#endif
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        IArrangedElement element = (IArrangedElement)entry.Key;

                        Debug.Assert(element.Container == container, "We have non-children in our containers cached bounds store.");
#if DEBUG
                        // We are about to set the bounds to the cached value. We clear the cached value
                        // before SetBounds because some controls fiddle with the bounds on SetBounds
                        // and will callback InitLayout with a different bounds and BoundsSpecified.
                        dictionary.Remove(entry.Key);
#endif
                        Rectangle bounds = (Rectangle)entry.Value!;
                        element.SetBounds(bounds, BoundsSpecified.None);
#if DEBUG
                        break;
                    }
#endif
                }

                ClearCachedBounds(container);
            }
        }

        private static void ClearCachedBounds(IArrangedElement container)
        {
            container.Properties.SetObject(s_cachedBoundsProperty, null);
        }

        private static void SetCachedBounds(IArrangedElement element, Rectangle bounds)
        {
            if (bounds != GetCachedBounds(element))
            {
                IDictionary? dictionary = element.Container?.Properties.GetObject(s_cachedBoundsProperty) as IDictionary;
                if (dictionary is null && element.Container is not null)
                {
                    dictionary = new HybridDictionary();
                    element.Container.Properties.SetObject(s_cachedBoundsProperty, dictionary);
                }

                dictionary![element] = bounds;
            }
        }

/*
        private static AnchorInfo GetAnchorInfo(IArrangedElement element)
        {
            return (AnchorInfo)element.Properties.GetObject(s_layoutInfoProperty);
        }

        internal static void SetAnchorInfo(IArrangedElement element, AnchorInfo value)
        {
            element.Properties.SetObject(s_layoutInfoProperty, value);
        }
*/
    /*    private protected override void InitLayoutCore(IArrangedElement element, BoundsSpecified specified)
        {
            Debug.Assert(specified == BoundsSpecified.None || GetCachedBounds(element) == element.Bounds,
                "Attempt to InitLayout while element has active cached bounds.");

            if (specified != BoundsSpecified.None && CommonProperties.GetNeedsAnchorLayout(element))
            {
                UpdateAnchorInfo(element);
            }
        }*/

        internal override Size GetPreferredSize(IArrangedElement container, Size proposedBounds)
        {
            Debug.Assert(!HasCachedBounds(container), "Do not call this method with an active cached bounds list.");

            TryCalculatePreferredSize(container, measureOnly: true, preferredSize: out Size prefSize);
            return prefSize;
        }

        private static Size GetAnchorPreferredSize(IArrangedElement container)
        {
            Size prefSize = Size.Empty;

            ArrangedElementCollection children = container.Children;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                Control? element = container.Children[i] as Control;
                if (element is not null && !CommonProperties.GetNeedsDockLayout(element) /*&& element.ParticipatesInLayout*/)
                {
                    //AnchorStyles anchor = GetAnchor(element);
                    Padding margin = CommonProperties.GetMargin(element);
                    Rectangle elementSpace = LayoutUtils.InflateRect(GetCachedBounds(element), margin);

                    if (IsAnchored(element, AnchorStyles.Left) && !IsAnchored(element, AnchorStyles.Right))
                    {
                        // If we are anchored to the left we make sure the container is large enough not to clip us
                        // (unless we are right anchored, in which case growing the container will just resize us.)
                        prefSize.Width = Math.Max(prefSize.Width, elementSpace.Right);
                    }

                    if (!IsAnchored(element, AnchorStyles.Bottom))
                    {
                        // If we are anchored to the top we make sure the container is large enough not to clip us
                        // (unless we are bottom anchored, in which case growing the container will just resize us.)
                        prefSize.Height = Math.Max(prefSize.Height, elementSpace.Bottom);
                    }

                    if (IsAnchored(element, AnchorStyles.Right))
                    {
                        // If we are right anchored, see what the anchor distance between our right edge and
                        // the container is, and make sure our container is large enough to accomodate us.
                        Rectangle anchorDest = GetAnchorDestination(element, Rectangle.Empty, /*measureOnly=*/true);
                        if (anchorDest.Width < 0)
                        {
                            prefSize.Width = Math.Max(prefSize.Width, elementSpace.Right + anchorDest.Width);
                        }
                        else
                        {
                            prefSize.Width = Math.Max(prefSize.Width, anchorDest.Right);
                        }
                    }

                    if (IsAnchored(element, AnchorStyles.Bottom))
                    {
                        // If we are right anchored, see what the anchor distance between our right edge and
                        // the container is, and make sure our container is large enough to accomodate us.
                        Rectangle anchorDest = GetAnchorDestination(element, Rectangle.Empty, /*measureOnly=*/true);
                        if (anchorDest.Height < 0)
                        {
                            prefSize.Height = Math.Max(prefSize.Height, elementSpace.Bottom + anchorDest.Height);
                        }
                        else
                        {
                            prefSize.Height = Math.Max(prefSize.Height, anchorDest.Bottom);
                        }
                    }
                }
            }

            return prefSize;
        }

        public static bool IsAnchored(Control element, AnchorStyles desiredAnchor)
        {
            switch (desiredAnchor)
            {
                case AnchorStyles.Left:
                    return element.Anchors?.Left != null;
                case AnchorStyles.Right:
                    return element.Anchors?.Right != null;
                case AnchorStyles.Top:
                    return element.Anchors?.Top != null;
                case AnchorStyles.Bottom:
                    return element.Anchors?.Bottom != null;
            }

            return false;
        }
    }
}
