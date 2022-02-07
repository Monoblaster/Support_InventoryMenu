datablock ShapeBaseImageData(InventoryMenuImage)
{
    shapeFile = "base/data/shapes/empty.dts";
    emap = true;

    stateName[0] = "PreparePreClick";
    stateWaitForTimeout[0] = true;
    stateTimeoutValue[0] = 0.1;
    stateTransitionOnTimeout[0] = "PreClick";

    stateName[1] = "PreClick";
    stateWaitForTimeout[1] = true;
    stateTimeoutValue[0] = 0.1;
    stateTransitionOnTriggerDown[1] = "Click";

    stateName[3] = "Click";
    stateScript[3] = "Click";
    stateWaitForTimeout[3] = true;
    stateTimeoutValue[0] = 0.1;
    stateTransitionOnTriggerUp[3] = "PreparePreClick";
};

function InventoryMenu_Push(%client,%menu)
{
    %player = %client.player;
    if(!isObject(%player))
    {
        //no player
        return;
    }

    if(%player.currInventoryMenuPos $= "")
    {
        %player.currInventoryMenuPos = %player.currTool;
    }

    %count = %player.InventroyMenuStack["Count"] + 0;

    //record the previous menu's position
    %player.InventroyMenuStack["Position",%count - 1] = %player.currInventoryMenuPos;

    //add a new menu to the player's menu stack
    %player.InventroyMenuStack["Menu",%count] = %menu;
    %player.InventroyMenuStack["Count"]++;

    //set active slot to the start pos
    %startPos = %menu.startPos;
    //make sure that the inventory is updated
    %player.previousViewOffset = -1;
    InventoryMenu_SetPos(%client,%startPos);
}

function InventoryMenu_Pop(%client)
{
    %player = %client.player;
    if(!isObject(%player))
    {
        //no player
        return;
    }

    %count = %player.InventroyMenuStack["Count"];

    if(%count == 0)
    {
        //no menu to pop
        return;
    }

    %count = %player.InventroyMenuStack["Count"]--;
    
    //restore their slot position
    %player.currInventoryMenuPos = %player.InventroyMenuStack["Position",%count - 1];

    if(%count == 0)
    {
        //restore actual inventory
        CommandToClient(%client,'SetActiveTool',%player.currInventoryMenuPos);

        %player.currInventoryMenuPos = "";
        //set the inventory display
        %datablock = %player.getDatablock();
        %maxTools = %datablock.maxTools;
        for(%i = 0; %i < %maxTools; %i++)
        {
            messageClient(%client, 'MsgItemPickup', "", %i, %player.tool[%i],true);
        }
    }   
    else
    {
        //restore the previous menu or if there are no menus left return to inventory
        //make sure that the inventory is updated
        %player.previousViewOffset = -1;
        InventoryMenu_SetPos(%client,%player.currInventoryMenuPos);
    }
}

function InventoryMenu_ClearAll(%client)
{
    %player = %client.player;
    if(!isObject(%player))
    {
        //no player
        return;
    }

    %player.InventroyMenuStack["Count"] = 0;
    InventoryMenu_Pop(%client);
}

package InventoryMenu
{
    function serverCmdUseTool(%client,%slot)
    {
        %player = %client.player;
        if(isObject(%player))
        {
            %count = %player.InventroyMenuStack["Count"];
            
            if(%count > 0)
            {
                if(%player.ignoreUseTool != 0)
                {
                    //buffer to prevent infinite loops from callbacks
                    %player.ignoreUseTool--;
                    return;
                }

                //record the move
                %prev = %player.previousSlot;
                if(%prev == -1)
                {
                    %prev = %slot;
                }
                %difference = %slot - %prev;

                //move the ui
                %newPos = %player.currInventoryMenuPos + %difference;

                //account for wrapping
                %datablock = %player.getDatablock();
                %maxTools = %datablock.maxTools;
                %menuCount = %player.InventroyMenuStack["Menu",%count - 1].getCount();
                if(%prev == 0 || %prev == (%maxTOols - 1))
                {
                    if(mAbs(%difference) >= (%maxTools - 1))
                    {
                        %newPos = mAbs(%player.currInventoryMenuPos - (%menuCount - 1));
                    }
                }

                InventoryMenu_SetPos(%client,%newPos);
                return;
            }
        }

        parent::serverCmdUseTool(%client,%slot);
    }
};
deactivatePackage("InventoryMenu");
activatePackage("InventoryMenu");

function InventoryMenu_SetPos(%client,%pos)
{
    %player = %client.player;
    if(!isObject(%player))
    {
        //no player
        return;
    }

    %menu = %player.InventroyMenuStack["Menu",%player.InventroyMenuStack["Count"] - 1];
    if(!isObject(%menu))
    {
        //no menu
        return;
    }

    //check if the menu was just opened
    %menuJustOpened = %player.currInventoryMenuPos == -1;

    //get position to be set
    %count = %menu.getCount();
    %pos = mClamp(%pos,0,%count - 1);
    %player.currInventoryMenuPos = %pos;

    //call the select callback
    %callback = getWord(%menu.getValue(%pos),2);
    if(!isFunction(%callback))
    {
        if(%callback !$= "")
        {
            warn("Inventory Selector: Item " @ %pos @ " select callback does not exist for menu " @ %menu.getName());
        }
    }
    else
    {
        call(%callback,%menu,%player,%pos);
    }


    //get the view offset
    %datablock = %player.getDatablock();
    %maxTools = %datablock.maxTools;

    %offset = mFloor(%maxTools / 2);
    %viewOffset = mClamp(%pos - %maxTools + %offset + 1, 0, %count - %offset - 3);
    %player.previousSlot = %pos - %viewOffset;

    //only update visual if we scrolled
    if(%player.previousViewOffset != %viewOffset)
    {
        //if the player inventory isn't open then msgItemPickup won't mess stuff up
        if(%player.currTool == -1 && %menuJustOpened)
        {
            %player.ignoreUseTool = 1;
        }
        else
        {
            %player.ignoreUseTool = 2;
        }

        //set the inventory display
        for(%i = 0; %i < %maxTools; %i++)
        {
            %item = getWord(%menu.getValue(%viewOffset + %i),0);
            if(isObject(%item))
            {
                %item = %item.getId();
            }
            messageClient(%client, 'MsgItemPickup', "", %i, %item,true);
        }

        //set the active tool slot to the correct one
        CommandToClient(%client,'SetActiveTool',%player.previousSlot);
    }

    //equip our special click detecting image
    if(%player.getMountedImage(0) != InventoryMenuImage.getId())
    {
        serverCmdUnuseTool(%client);
        %player.mountImage(InventoryMenuImage,0);
    }
    
    %player.previousViewOffset = %viewOffset;
}

function InventoryMenuImage::Click(%data,%player)
{
    %pos = %player.currInventoryMenuPos;
    %menu = %player.InventroyMenuStack["Menu",%player.InventroyMenuStack["Count"] - 1];
    %count = %menu.getCount();

    if(%pos >= %count)
    {
        //out of bounds
        return;
    }

    %callback = getWord(%menu.getValue(%pos),1);

    if(!isFunction(%callback))
    {
        if(%callback !$= "")
        {
            warn("Inventory Selector: Item " @ %pos @ " click callback does not exist for menu " @ %menu.getName());
        }
    }
    else
    {
        call(%callback,%menu,%player,%pos);
    }
}

//create the object to be referenced when opening a menu
function InventoryMenu_Create(%name)
{
    //create menu object
    %menu = new ScriptObject(%name)
	{
		superClass = "List";
        class = "InventoryMenu";
	};
    return %menu;
}

function InventoryMenu::AddItem(%menu,%item,%callBack,%selectCallback,%row)
{
    %menuItem = "InventoryMenuItem" @ %item ;
    if(isObject(%menuItem))
    {
        %menu.add(%menuItem SPC %callback SPC %selectCallback, %row);
    }
    else
    {
        //menu item doesn't exit
        warn("Inventory Selector: Menu item " @ %item @ " does not exist");
    }
}

function InventoryMenu::setItem(%menu,%row,%item,%useCallback,%selectCallback)
{
    %menuItem = "InventoryMenuItem" @ %item ;
    if(isObject(%menuItem))
    {
        %menu.set(%row, %menuItem SPC %useCallback SPC %selectCallback);
    }
    else
    {
        //menu item doesn't exit
        warn("Inventory Selector: Menu item " @ %item @ " does not exist");
    }
}

function InventoryMenuItem_Create(%refName,%name,%iconPath,%colorShift,%colorShiftColor)
{
    %dataName = "InventoryMenuItem" @ %refName; 

    if(isObject(%dataName))
    {
        //datablock already exists smh
        warn("Inventory Selector: Menu item " @ %refName @ " already exists overwriting");
    }

    %c = -1;
    %MenuItemCode[%c++] = "datablock ItemData(" @ %dataName @")";
    %MenuItemCode[%c++] = "{";
    %MenuItemCode[%c++] =    "catagory = \"Tools\";";
    %MenuItemCode[%c++] =    "shapefile = \"base/data/shapes/empty.dts\";";
    %MenuItemCode[%c++] =    "mass = 1;";
    %MenuItemCode[%c++] =    "density = 0.2;";
    %MenuItemCode[%c++] =    "elasticity = 0.2;";
    %MenuItemCode[%c++] =    "friction = 0.6;";
    %MenuItemCode[%c++] =    "emap = 1;";
    %MenuItemCode[%c++] =    "uiName = %name;";
    %MenuItemCode[%c++] =    "iconName = %iconPath;";
    %MenuItemCode[%c++] =    "doColorShift = %colorShift;";
    %MenuItemCode[%c++] =    "colorShiftColor = %colorShiftColor;";
    %MenuItemCode[%c++] =    "canDrop = false;";
    %MenuItemCode[%c++] = "};";
    %MenuItemCode[%c++] = "return %dataName;";
    %c++;

    for(%i = 0; %i < %c; %i++)
    {
        %MenuItemDatablock = %MenuItemDatablock @ %MenuItemCode[%i];
    }

    %newData = eval(%MenuItemDatablock);
    return %refName;
}

function List_NewList()
{
    %list = new ScriptObject()
	{
		class = "List";
	};

    return %list;
}

function List::ListValues(%list)
{
    %count = %list.getcount();
    for(%i = 0; %i < %count; %i++)
    {
        echo(%list.getvalue(%i));
    }
}

function List::IsTag(%list,%tag)
{
	return %list.row[%tag] !$= "";
}

function List::GettagString(%list)
{
    return %list.string;
}

function List::GetCount(%list)
{
    return getWordCount(%list.string);
}

function List::GetRow(%list,%tag)
{
    return %list.row[%tag];
}

function List::GetTag(%list,%row)
{
    return getWord(%list.string,%row);
}

function List::GetValue(%list,%row)
{
    return %list.value[%list.getTag(%row)];
}

function List::FindValue(%list,%Value)
{
    %count = %list.getCount();
    for(%i = 0; %i < %count; %i++)
    {
        %thisValue = %list.getValue(%i);
        if(strPos(%thisValue,%Value) >= 0)
        {
            return %i;
        }
    }
    return -1;
}

function List::Add(%list,%Value,%row,%tag)
{
    %count = %list.getCount();
    if(%row < 0 || %row >= %count || %row $= "")
    {
        %row = %count;
    }
    for(%i = %count; %i > %row; %i--)
    {
        %list.Swap(%i,%i - 1);
    }
    %list.Set(%row,%Value,%tag);
}

function List::Set(%list,%row,%Value,%tag)
{
	if(%tag $= "")
	{
		%tag = %list.adds + 0;
	}
	%list.adds++;

	%safety = 0;
	while(%list.istag(%tag))
	{
		%tag = getRandom(0,999999);
		%safety++;
		
		if(%safety > 100)
		{
			warn("Lists: Set tag failure");
			return "";
		}
	}
	
    %list.string = setWord(%list.string,%row,%tag);
    %list.row[%tag] = %row;
    %list.Value[%tag] = %Value;
}

function List::Swap(%list,%row1,%row2)
{
    %tempValue1 = %list.getValue(%row1);
    %temptag1 = %list.gettag(%row1);
	%list.row[%temptag1] = "";
	
	%tempValue2 = %list.getValue(%row2);
    %temptag2 = %list.gettag(%row2);
	%list.row[%temptag2] = "";

    %list.Set(%row1,%tempValue2,%temptag2);
    %list.Set(%row2,%tempValue1,%temptag1);
}

function List::Remove(%list,%row)
{
    %tag = %list.gettag(%row);
    if(!%list.istag(%tag))
	{
		return false;
	}
	
    %list.string = removeWord(%list.string,%row);
    %list.Value[%tag] = "";
    %list.row[%tag] = "";
	return true;
}

function List::Clear(%list)
{
    %count = %list.getCount();
    for(%i = %count - 1; %i >= 0; %i--)
    {
        %list.remove(%i);
    }
}

function List::Sort(%list,%reverse,%sortFunction)
{
	if(%sortFunction $= "" || !isFunction(%sortFunction))
	{
		%sortFunction = "list_numericalSort";
	}

	list_quicksort(%list,0,%list.getCount() - 1,%reverse,%sortFunction);
}

function list_quicksort(%list,%lo,%hi,%reverse,%sortFunction)
{
	if(%lo >= 0 && %hi >= 0 && %lo < %hi)
	{
		%partition = list_partition(%list,%lo,%hi,%reverse,%sortFunction);
		list_quicksort(%list,%lo,%partition,%reverse,%sortFunction);
		list_quicksort(%list,%partition + 1,%hi,%reverse,%sortFunction);
	}
}

function list_partition(%list,%lo,%hi,%reverse,%sortFunction)
{
	%pivot = %list.getValue(mFloor((%hi + %lo) / 2));
	
	%i = %lo - 1;
	%j = %hi + 1;
	while(true)
	{
		%i = call(%sortFunction,%list,%pivot,%i,1,%reverse);

		%j = call(%sortFunction,%list,%pivot,%j,-1,!%reverse);

		if(%i >= %j)
		{
			return %j;
		}
		
		%list.swap(%i, %j);
	}
}

function list_numericalSort(%list,%pivot,%i,%direction,%reverse)
{
	%modifier = 1;
	if(%reverse)
	{
		%modifier = -1;
	}
    while(%list.getValue(%i += %direction) * %modifier < %pivot * %modifier){}
	
	return %i;
}