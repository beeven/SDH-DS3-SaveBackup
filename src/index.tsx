import {
  ButtonItem,
  definePlugin,
  //DialogButton,

  PanelSection,
  PanelSectionRow,
  //Router,
  ServerAPI,
  staticClasses,
  DropdownItem,
  DropdownOption
} from "decky-frontend-lib";
import { VFC, useState, useEffect, useMemo } from "react";
import { FaDragon } from "react-icons/fa";

//import logo from "../assets/logo.png";

interface BackupMethodArgs {
  is_save: boolean;
}

interface SlotConfig {
  selectedSlot: number;
  slots: SlotInfo[]
}
interface SlotInfo {
  lastModified: string;
}

const Content: VFC<{ serverAPI: ServerAPI }> = ({ serverAPI }) => {
  const [statusText, setStatusText] = useState<string | undefined>();

  const onSaveLoad = async (save: boolean) => {
    const result = await serverAPI.callPluginMethod<BackupMethodArgs, string>(
      "save_load",
      {
        is_save: save,
      }
    );
    if (result.success) {
      setStatusText(result.result);
      const configresult = await serverAPI.callPluginMethod<any,SlotConfig>("get_slot_config",{});
      if(configresult.success)
      {
        setSlotConfig(configresult.result);
      }
    }
  };

  const [slotConfig, setSlotConfig] = useState<SlotConfig | null>(null);
  const [selectedSlot, setSelectedSlot] = useState<number | null>(null);

  const onSelectionChange = async (slot: number) =>{
    setSelectedSlot(slot);
    await serverAPI.callPluginMethod("select_slot",{ slot: slot })
  }

  useEffect(()=>{
  serverAPI.callPluginMethod<any,SlotConfig>("get_slot_config", {}).then(result =>{
    if(result.success) {
      setSlotConfig(result.result);
      setSelectedSlot(result.result.selectedSlot);
    }
  });
}, []);

  const slotOptions = useMemo(
    (): DropdownOption[] => {
      let slotCount = slotConfig?.slots?.length;
      if(slotCount != null && typeof(slotCount) != 'undefined') {
        let slots = [];
        for(let i=0; i<slotCount; i++)
        {
          let timestamp = slotConfig?.slots[i]?.lastModified;
          if(timestamp === null || typeof(timestamp) === "undefined")
          {
            timestamp = "Empty"
          }
          let entry = {data: i+1, label: (i+1).toString() + " " +  timestamp};
          slots.push(entry)
        }
        return slots;
      }
      else {
        return [
          { data: 1, label: "1 Empty"},
          { data: 2, label: "2 Empty"},
          { data: 3, label: "3 Empty"},
        ]
      }
    },
    [slotConfig]
  );
  

  return (
    <PanelSection title="Save Backup">
      <PanelSectionRow>
        <ButtonItem
          layout="below"
          onClick={() => onSaveLoad(true)}
        >
          Save
        </ButtonItem>
      </PanelSectionRow>

      <PanelSectionRow>
        <ButtonItem
          layout="below"
          onClick={() => onSaveLoad(false)}
        >
          Load
        </ButtonItem>
      </PanelSectionRow>
      <PanelSectionRow>
        <div>{statusText}</div>
      </PanelSectionRow>
      <PanelSectionRow>
        <DropdownItem 
          label = "Save Slot #"
          menuLabel="Slot #"
          strDefaultLabel="Select a slot"
          rgOptions={slotOptions}
          selectedOption = {selectedSlot}
          onChange={(data) => onSelectionChange(data.data)}
        />
      </PanelSectionRow>
    </PanelSection>
  );
};

// const DeckyPluginRouterTest: VFC = () => {
//   return (
//     <div style={{ marginTop: "50px", color: "white" }}>
//       Hello World!
//       <DialogButton onClick={() => Router.NavigateToStore()}>
//         Go to Store
//       </DialogButton>
//     </div>
//   );
// };

export default definePlugin((serverApi: ServerAPI) => {
  // serverApi.routerHook.addRoute("/decky-plugin-test", DeckyPluginRouterTest, {
  //   exact: true,
  // });


  return {
    title: <div className={staticClasses.Title}>DS3 SaveBackup</div>,
    content: <Content serverAPI={serverApi} />,
    icon: <FaDragon />,
    onDismount() {
      //serverApi.routerHook.removeRoute("/decky-plugin-test");
    },
  };
});
